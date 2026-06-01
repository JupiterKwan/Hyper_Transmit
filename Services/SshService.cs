using Hyper_Transmit.Models;
using Hyper_Transmit.Models.Enums;
using Hyper_Transmit.Services.Interfaces;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hyper_Transmit.Services
{
    /// <summary>
    /// SSH/SFTP service implementation using SSH.NET library.
    /// </summary>
    public class SshService : ISshService
    {
        private SftpClient? _sftpClient;
        private SshClient? _sshClient;
        private ConnectionConfig? _config;
        private readonly SemaphoreSlim _transferSemaphore = new(1, 1);
        private readonly CredentialService _credentialService = new();

        public bool IsConnected => _sftpClient?.IsConnected == true;
        public string? CurrentHost => _config?.Host;
        public string? LastError { get; private set; }

        public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

        public async Task ConnectAsync(ConnectionConfig config, CancellationToken ct = default)
        {
            await Task.Run(() =>
            {
                Disconnect();

                _config = config;

                // Decrypt password if it's encrypted
                var decryptedConfig = new ConnectionConfig
                {
                    Id = config.Id,
                    Name = config.Name,
                    Host = config.Host,
                    Port = config.Port,
                    Username = config.Username,
                    EncryptedPassword = _credentialService.Decrypt(config.EncryptedPassword),
                    PrivateKeyPath = config.PrivateKeyPath,
                    EncryptedPassphrase = _credentialService.Decrypt(config.EncryptedPassphrase),
                    AuthType = config.AuthType,
                    Protocol = config.Protocol,
                    RemoteDefaultPath = config.RemoteDefaultPath
                };

                var authMethods = BuildAuthMethods(decryptedConfig);
                if (authMethods.Count == 0)
                    throw new InvalidOperationException("No authentication methods configured. Please set a password or private key.");

                // Log connection attempt for debugging
                System.Diagnostics.Debug.WriteLine($"Attempting SSH connection to {config.Host}:{config.Port} as {config.Username}");
                System.Diagnostics.Debug.WriteLine($"Password length: {decryptedConfig.EncryptedPassword?.Length ?? 0}");
                System.Diagnostics.Debug.WriteLine($"Auth type: {decryptedConfig.AuthType}");

                var connectionInfo = new ConnectionInfo(config.Host, config.Port, config.Username, authMethods.ToArray())
                {
                    Timeout = TimeSpan.FromSeconds(30),
                    RetryAttempts = 3
                };

                ct.ThrowIfCancellationRequested();

                try
                {
                    _sftpClient = new SftpClient(connectionInfo);

                    // Accept any host key (TODO: store and verify fingerprints later)
                    _sftpClient.HostKeyReceived += (sender, e) => { e.CanTrust = true; };

                    _sftpClient.Connect();

                    // Create SSH client sharing the same connection info (lazy - only connect when needed)
                    _sshClient = new SshClient(connectionInfo);
                    _sshClient.HostKeyReceived += (sender, e) => { e.CanTrust = true; };

                    ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(true,
                        $"Connected to {config.Host}"));
                }
                catch (Renci.SshNet.Common.SshAuthenticationException authEx)
                {
                    Disconnect();
                    LastError = $"Authentication failed: {authEx.Message}";
                    return;
                }
                catch (Renci.SshNet.Common.SshConnectionException connEx)
                {
                    Disconnect();
                    LastError = $"Connection failed: {connEx.Message}";
                    return;
                }
                catch (System.Net.Sockets.SocketException sockEx)
                {
                    Disconnect();
                    LastError = $"Network error: {sockEx.Message}";
                    return;
                }
                catch (Exception ex)
                {
                    Disconnect();
                    LastError = $"Connection error: {ex.Message}";
                    return;
                }
            }, ct);
        }

        public async Task DisconnectAsync()
        {
            await Task.Run(() => Disconnect());
        }

        private void Disconnect()
        {
            try
            {
                if (_sftpClient?.IsConnected == true)
                    _sftpClient.Disconnect();
                _sftpClient?.Dispose();
                _sftpClient = null;

                if (_sshClient?.IsConnected == true)
                    _sshClient.Disconnect();
                _sshClient?.Dispose();
                _sshClient = null;

                ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(false, "Disconnected"));
            }
            catch
            {
                // Ignore disconnect errors
            }
            _config = null;
        }

        public async Task<IReadOnlyList<RemoteFileInfo>> ListDirectoryAsync(string path, CancellationToken ct = default)
        {
            EnsureConnected();

            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var entries = _sftpClient!.ListDirectory(path);
                var result = new List<RemoteFileInfo>();

                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();

                    // Skip . and .. entries
                    if (entry.Name == "." || entry.Name == "..")
                        continue;

                    result.Add(new RemoteFileInfo
                    {
                        Name = entry.Name,
                        FullPath = entry.FullName,
                        Size = (long)entry.Length,
                        IsDirectory = entry.IsDirectory,
                        IsSymlink = entry.IsSymbolicLink,
                        LastModified = entry.LastWriteTime,
                        Owner = entry.UserId.ToString(),
                        Group = entry.GroupId.ToString(),
                        Permissions = FormatPermissions(entry.Attributes),
                        PermissionsOctal = ComputeOctal(entry.Attributes)
                    });
                }

                // Sort: directories first, then by name
                return (IReadOnlyList<RemoteFileInfo>)result
                    .OrderByDescending(f => f.IsDirectory)
                    .ThenBy(f => f.Name)
                    .ToList()
                    .AsReadOnly();
            }, ct);
        }

        public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        {
            EnsureConnected();
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                _sftpClient!.CreateDirectory(path);
            }, ct);
        }

        public async Task DeleteFileAsync(string path, CancellationToken ct = default)
        {
            EnsureConnected();
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                _sftpClient!.DeleteFile(path);
            }, ct);
        }

        public async Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default)
        {
            EnsureConnected();
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (recursive)
                {
                    DeleteDirectoryRecursive(path, ct);
                }
                else
                {
                    _sftpClient!.DeleteDirectory(path);
                }
            }, ct);
        }

        private void DeleteDirectoryRecursive(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var entries = _sftpClient!.ListDirectory(path);

            foreach (var entry in entries)
            {
                if (entry.Name == "." || entry.Name == "..")
                    continue;

                if (entry.IsDirectory)
                {
                    DeleteDirectoryRecursive(entry.FullName, ct);
                }
                else
                {
                    _sftpClient.DeleteFile(entry.FullName);
                }
            }

            _sftpClient.DeleteDirectory(path);
        }

        public async Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
        {
            EnsureConnected();
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                _sftpClient!.RenameFile(oldPath, newPath);
            }, ct);
        }

        public async Task<RemoteFileInfo> GetFileInfoAsync(string path, CancellationToken ct = default)
        {
            EnsureConnected();
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var attrs = _sftpClient!.GetAttributes(path);
                var name = path.Split('/').Last();

                return new RemoteFileInfo
                {
                    Name = name,
                    FullPath = path,
                    Size = (long)attrs.Size,
                    IsDirectory = attrs.IsDirectory,
                    IsSymlink = attrs.IsSymbolicLink,
                    LastModified = attrs.LastWriteTime,
                    Owner = attrs.UserId.ToString(),
                    Group = attrs.GroupId.ToString(),
                    Permissions = FormatPermissions(attrs),
                    PermissionsOctal = ComputeOctal(attrs)
                };
            }, ct);
        }

        public async Task SetPermissionsAsync(string path, int permissions, CancellationToken ct = default)
        {
            EnsureConnected();
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var attrs = _sftpClient!.GetAttributes(path);
                // SSH.NET uses FileMode for permissions
                attrs.SetPermissions(unchecked((short)permissions));
                _sftpClient.SetAttributes(path, attrs);
            }, ct);
        }

        public async Task UploadFileAsync(string localPath, string remotePath,
            IProgress<TransferProgress>? progress, CancellationToken ct = default,
            ManualResetEventSlim? pauseEvent = null)
        {
            EnsureConnected();

            await _transferSemaphore.WaitAsync(ct);
            try
            {
                var cancelled = await Task.Run(() =>
                {
                    var fileInfo = new FileInfo(localPath);
                    var totalSize = fileInfo.Length;
                    var stopwatch = Stopwatch.StartNew();

                    using var fileStream = File.OpenRead(localPath);
                    using var pausableStream = pauseEvent != null
                        ? new PausableStream(fileStream, pauseEvent)
                        : null;
                    var streamToUse = pausableStream ?? (Stream)fileStream;

                    _sftpClient!.UploadFile(streamToUse, remotePath, true, uploaded =>
                    {
                        if (ct.IsCancellationRequested) return;

                        // Check pause before reporting progress
                        pauseEvent?.Wait(ct);
                        if (ct.IsCancellationRequested) return;

                        var elapsed = stopwatch.Elapsed;
                        var speed = elapsed.TotalSeconds > 0 ? (long)uploaded / elapsed.TotalSeconds : 0;
                        progress?.Report(new TransferProgress((long)uploaded, totalSize, speed, elapsed));
                    });

                    return ct.IsCancellationRequested;
                });

                if (cancelled)
                    throw new OperationCanceledException(ct);
            }
            finally
            {
                _transferSemaphore.Release();
            }
        }

        public async Task DownloadFileAsync(string remotePath, string localPath,
            IProgress<TransferProgress>? progress, CancellationToken ct = default,
            ManualResetEventSlim? pauseEvent = null)
        {
            EnsureConnected();

            await _transferSemaphore.WaitAsync(ct);
            try
            {
                var cancelled = await Task.Run(() =>
                {
                    if (_sftpClient == null || !_sftpClient.IsConnected)
                    {
                        return true;
                    }

                    try
                    {
                        var attrs = _sftpClient.GetAttributes(remotePath);
                        var totalSize = (long)attrs.Size;
                        var stopwatch = Stopwatch.StartNew();

                        var parentDir = Path.GetDirectoryName(localPath);
                        if (!string.IsNullOrEmpty(parentDir))
                            Directory.CreateDirectory(parentDir);

                        if (Directory.Exists(localPath))
                            Directory.Delete(localPath, true);

                        bool appendForResume = false;
                        long resumePosition = 0;
                        if (File.Exists(localPath))
                        {
                            var existingSize = new FileInfo(localPath).Length;
                            if (existingSize > 0 && existingSize < totalSize)
                            {
                                appendForResume = true;
                                resumePosition = existingSize;
                            }
                        }

                        FileStream fileStream;
                        if (appendForResume)
                        {
                            fileStream = new FileStream(localPath, FileMode.Append, FileAccess.Write, FileShare.None, 8192, true);
                            progress?.Report(new TransferProgress(resumePosition, totalSize, 0, stopwatch.Elapsed));
                        }
                        else
                        {
                            fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                        }

                        using (fileStream)
                        {
                            if (appendForResume)
                            {
                                _sftpClient.DownloadFile(remotePath, fileStream, downloaded =>
                                {
                                    if (ct.IsCancellationRequested) return;
                                    pauseEvent?.Wait(ct);
                                    if (ct.IsCancellationRequested) return;

                                    var elapsed = stopwatch.Elapsed;
                                    var totalDownloaded = resumePosition + (long)downloaded;
                                    var speed = elapsed.TotalSeconds > 0 ? totalDownloaded / elapsed.TotalSeconds : 0;
                                    progress?.Report(new TransferProgress(totalDownloaded, totalSize, (long)speed, elapsed));
                                });
                            }
                            else
                            {
                                _sftpClient.DownloadFile(remotePath, fileStream, downloaded =>
                                {
                                    if (ct.IsCancellationRequested) return;
                                    pauseEvent?.Wait(ct);
                                    if (ct.IsCancellationRequested) return;

                                    var elapsed = stopwatch.Elapsed;
                                    var speed = elapsed.TotalSeconds > 0 ? (long)downloaded / elapsed.TotalSeconds : 0;
                                    progress?.Report(new TransferProgress((long)downloaded, totalSize, speed, elapsed));
                                });
                            }
                        }

                        return ct.IsCancellationRequested;
                    }
                    catch (Renci.SshNet.Common.SshException)
                    {
                        return true;
                    }
                    catch (System.IO.IOException)
                    {
                        return true;
                    }
                });

                if (cancelled)
                    throw new OperationCanceledException(ct);
            }
            finally
            {
                _transferSemaphore.Release();
            }
        }

        public async Task UploadDirectoryAsync(string localDir, string remoteDir,
            IProgress<TransferProgress>? progress, CancellationToken ct = default,
            ManualResetEventSlim? pauseEvent = null)
        {
            EnsureConnected();

            await Task.Run(async () =>
            {
                long totalDirSize = GetDirectorySize(localDir);
                var tracker = new ProgressTracker(totalDirSize);
                await UploadDirectoryRecursiveAsync(localDir, remoteDir, tracker, progress, ct, pauseEvent);
            }, ct);
        }

        private async Task UploadDirectoryRecursiveAsync(string localDir, string remoteDir, ProgressTracker tracker,
            IProgress<TransferProgress>? progress, CancellationToken ct, ManualResetEventSlim? pauseEvent = null)
        {
            // Create remote directory if it doesn't exist
            try { _sftpClient!.CreateDirectory(remoteDir); }
            catch { /* directory may already exist */ }

            // Upload all files in the directory
            foreach (var file in Directory.GetFiles(localDir))
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var remotePath = remoteDir.TrimEnd('/') + "/" + fileName;

                // Create per-file progress that wraps into cumulative progress
                var currentFile = fileName;
                var fileProgress = new Progress<TransferProgress>(p =>
                {
                    var cumulativeBytes = tracker.CompletedBytes + p.BytesTransferred;
                    var speed = tracker.Elapsed > 0 ? cumulativeBytes / tracker.Elapsed : 0;
                    progress?.Report(new TransferProgress(cumulativeBytes, tracker.TotalBytes, (long)speed, tracker.Stopwatch.Elapsed, currentFile));
                });

                await UploadFileAsync(file, remotePath, fileProgress, ct, pauseEvent);
                tracker.CompletedBytes += new FileInfo(file).Length;
            }

            // Recursively upload subdirectories
            foreach (var dir in Directory.GetDirectories(localDir))
            {
                ct.ThrowIfCancellationRequested();
                var dirName = Path.GetFileName(dir);
                var remotePath = remoteDir.TrimEnd('/') + "/" + dirName;
                await UploadDirectoryRecursiveAsync(dir, remotePath, tracker, progress, ct, pauseEvent);
            }
        }

        private static long GetDirectorySize(string path)
        {
            try
            {
                long size = 0;
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    size += new FileInfo(file).Length;
                }
                return size;
            }
            catch { return 0; }
        }

        public async Task DownloadDirectoryAsync(string remoteDir, string localDir,
            IProgress<TransferProgress>? progress, CancellationToken ct = default,
            ManualResetEventSlim? pauseEvent = null)
        {
            EnsureConnected();

            await Task.Run(async () =>
            {
                Directory.CreateDirectory(localDir);
                long totalDirSize = GetRemoteDirectorySize(remoteDir);
                var tracker = new ProgressTracker(totalDirSize);
                await DownloadDirectoryRecursiveAsync(remoteDir, localDir, tracker, progress, ct, pauseEvent);
            }, ct);
        }

        private async Task DownloadDirectoryRecursiveAsync(string remoteDir, string localDir, ProgressTracker tracker,
            IProgress<TransferProgress>? progress, CancellationToken ct, ManualResetEventSlim? pauseEvent = null)
        {
            Directory.CreateDirectory(localDir);
            var entries = _sftpClient!.ListDirectory(remoteDir);

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Name == "." || entry.Name == "..") continue;

                if (entry.IsDirectory)
                {
                    var localPath = Path.Combine(localDir, entry.Name);
                    await DownloadDirectoryRecursiveAsync(entry.FullName, localPath, tracker, progress, ct);
                }
                else
                {
                    var localPath = Path.Combine(localDir, entry.Name);
                    var fileSize = (long)entry.Length;

                    var currentFile = entry.Name;
                    var fileProgress = new Progress<TransferProgress>(p =>
                    {
                        var cumulativeBytes = tracker.CompletedBytes + p.BytesTransferred;
                        var speed = tracker.Elapsed > 0 ? cumulativeBytes / tracker.Elapsed : 0;
                        progress?.Report(new TransferProgress(cumulativeBytes, tracker.TotalBytes, (long)speed, tracker.Stopwatch.Elapsed, currentFile));
                    });

                    await DownloadFileAsync(entry.FullName, localPath, fileProgress, ct, pauseEvent);
                    tracker.CompletedBytes += fileSize;
                }
            }
        }

        private long GetRemoteDirectorySize(string remoteDir)
        {
            try
            {
                long size = 0;
                var entries = _sftpClient!.ListDirectory(remoteDir);
                foreach (var entry in entries)
                {
                    if (entry.Name == "." || entry.Name == "..") continue;
                    if (entry.IsDirectory)
                        size += GetRemoteDirectorySize(entry.FullName);
                    else
                        size += (long)entry.Length;
                }
                return size;
            }
            catch { return 0; }
        }

        public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
        {
            EnsureSshConnected();

            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var cmd = _sshClient!.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromSeconds(30);
                var result = cmd.Execute();
                return result;
            }, ct);
        }

        private void EnsureConnected()
        {
            if (_sftpClient == null || !_sftpClient.IsConnected)
                throw new InvalidOperationException("Not connected to SFTP server.");
        }

        private void EnsureSshConnected()
        {
            if (_sshClient == null || !_sshClient.IsConnected)
                throw new InvalidOperationException("Not connected to SSH server.");
        }

        private static List<AuthenticationMethod> BuildAuthMethods(ConnectionConfig config)
        {
            var methods = new List<AuthenticationMethod>();

            switch (config.AuthType)
            {
                case AuthenticationType.Password:
                    methods.Add(new PasswordAuthenticationMethod(config.Username, config.EncryptedPassword));
                    break;

                case AuthenticationType.PrivateKey:
                    if (!string.IsNullOrEmpty(config.PrivateKeyPath))
                    {
                        var keyFile = new PrivateKeyFile(config.PrivateKeyPath, config.EncryptedPassphrase);
                        methods.Add(new PrivateKeyAuthenticationMethod(config.Username, keyFile));
                    }
                    break;

                case AuthenticationType.KeyAndPassword:
                    if (!string.IsNullOrEmpty(config.PrivateKeyPath))
                    {
                        var keyFile = new PrivateKeyFile(config.PrivateKeyPath, config.EncryptedPassphrase);
                        methods.Add(new PrivateKeyAuthenticationMethod(config.Username, keyFile));
                    }
                    if (!string.IsNullOrEmpty(config.EncryptedPassword))
                    {
                        methods.Add(new PasswordAuthenticationMethod(config.Username, config.EncryptedPassword));
                    }
                    break;
            }

            return methods;
        }

        private static int ComputeOctal(SftpFileAttributes attrs)
        {
            int mode = 0;
            if (attrs.OwnerCanRead) mode |= 256;    // 0400
            if (attrs.OwnerCanWrite) mode |= 128;   // 0200
            if (attrs.OwnerCanExecute) mode |= 64;  // 0100
            if (attrs.GroupCanRead) mode |= 32;     // 0040
            if (attrs.GroupCanWrite) mode |= 16;    // 0020
            if (attrs.GroupCanExecute) mode |= 8;   // 0010
            if (attrs.OthersCanRead) mode |= 4;     // 0004
            if (attrs.OthersCanWrite) mode |= 2;    // 0002
            if (attrs.OthersCanExecute) mode |= 1;  // 0001
            return mode;
        }

        private static string FormatPermissions(SftpFileAttributes attrs)
        {
            var sb = new StringBuilder();

            // Owner
            sb.Append(attrs.OwnerCanRead ? 'r' : '-');
            sb.Append(attrs.OwnerCanWrite ? 'w' : '-');
            sb.Append(attrs.OwnerCanExecute ? 'x' : '-');

            // Group
            sb.Append(attrs.GroupCanRead ? 'r' : '-');
            sb.Append(attrs.GroupCanWrite ? 'w' : '-');
            sb.Append(attrs.GroupCanExecute ? 'x' : '-');

            // Others
            sb.Append(attrs.OthersCanRead ? 'r' : '-');
            sb.Append(attrs.OthersCanWrite ? 'w' : '-');
            sb.Append(attrs.OthersCanExecute ? 'x' : '-');

            return sb.ToString();
        }

        /// <summary>
        /// Thread-safe tracker for cumulative directory transfer progress.
        /// </summary>
        private class ProgressTracker
        {
            public long TotalBytes { get; }
            public long CompletedBytes { get; set; }
            public Stopwatch Stopwatch { get; }
            public double Elapsed => Stopwatch.Elapsed.TotalSeconds;

            public ProgressTracker(long totalBytes)
            {
                TotalBytes = totalBytes;
                Stopwatch = Stopwatch.StartNew();
            }
        }

        public void Dispose()
        {
            Disconnect();
            _transferSemaphore.Dispose();
        }
    }

    /// <summary>
    /// A wrapper stream that blocks Read/Write operations when paused (PauseEvent is not signaled).
    /// This allows the SFTP connection to stay alive while the transfer is suspended.
    /// </summary>
    internal class PausableStream : Stream
    {
        private readonly Stream _inner;
        private readonly ManualResetEventSlim _pauseEvent;

        public PausableStream(Stream inner, ManualResetEventSlim pauseEvent)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _pauseEvent = pauseEvent ?? throw new ArgumentNullException(nameof(pauseEvent));
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);

        public override int Read(byte[] buffer, int offset, int count)
        {
            _pauseEvent.Wait(); // Block here when paused
            return _inner.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _pauseEvent.Wait(); // Block here when paused
            _inner.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Do NOT dispose the inner stream - caller owns it
            }
            base.Dispose(disposing);
        }
    }
}
