using Hyper_Transmit.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hyper_Transmit.Services.Interfaces
{
    /// <summary>
    /// Event args for connection state changes.
    /// </summary>
    public class ConnectionStateChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; }
        public string? Message { get; }

        public ConnectionStateChangedEventArgs(bool isConnected, string? message = null)
        {
            IsConnected = isConnected;
            Message = message;
        }
    }

    /// <summary>
    /// Service for SSH/SFTP communication with remote servers.
    /// </summary>
    public interface ISshService : IDisposable
    {
        bool IsConnected { get; }

        string? CurrentHost { get; }

        string? LastError { get; }

        event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

        /// <summary>
        /// Connects to a remote server using the provided configuration.
        /// </summary>
        Task ConnectAsync(ConnectionConfig config, CancellationToken ct = default);

        /// <summary>
        /// Disconnects from the remote server.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Lists files and directories in the specified remote path.
        /// </summary>
        Task<IReadOnlyList<RemoteFileInfo>> ListDirectoryAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Creates a directory on the remote server.
        /// </summary>
        Task CreateDirectoryAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Deletes a file on the remote server.
        /// </summary>
        Task DeleteFileAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Deletes a directory on the remote server.
        /// </summary>
        Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default);

        /// <summary>
        /// Renames/moves a file or directory on the remote server.
        /// </summary>
        Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default);

        /// <summary>
        /// Gets information about a single file or directory.
        /// </summary>
        Task<RemoteFileInfo> GetFileInfoAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Sets permissions on a remote file or directory.
        /// </summary>
        Task SetPermissionsAsync(string path, int permissions, CancellationToken ct = default);

        /// <summary>
        /// Uploads a local file to the remote server.
        /// </summary>
        Task UploadFileAsync(string localPath, string remotePath,
            IProgress<TransferProgress>? progress, CancellationToken ct = default,
            ManualResetEventSlim? pauseEvent = null);

        /// <summary>
        /// Downloads a remote file to the local file system.
        /// </summary>
        Task DownloadFileAsync(string remotePath, string localPath,
            IProgress<TransferProgress>? progress, CancellationToken ct = default,
            ManualResetEventSlim? pauseEvent = null);

        /// <summary>
        /// Uploads a local directory (recursively) to the remote server.
        /// </summary>
        Task UploadDirectoryAsync(string localDir, string remoteDir,
            IProgress<TransferProgress>? progress, CancellationToken ct = default,
            ManualResetEventSlim? pauseEvent = null);

        /// <summary>
        /// Downloads a remote directory (recursively) to the local file system.
        /// </summary>
        Task DownloadDirectoryAsync(string remoteDir, string localDir,
            IProgress<TransferProgress>? progress, CancellationToken ct = default,
            ManualResetEventSlim? pauseEvent = null);

        /// <summary>
        /// Executes a command on the remote server via SSH.
        /// </summary>
        Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default);
    }
}