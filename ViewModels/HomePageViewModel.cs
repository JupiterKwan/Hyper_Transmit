using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyper_Transmit.Models;
using Hyper_Transmit.Models.Enums;
using Hyper_Transmit.Services;
using Hyper_Transmit.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hyper_Transmit.ViewModels
{
    /// <summary>
    /// ViewModel for the Home page with dual-panel file browser.
    /// Manages the SSH connection and coordinates local/remote file panels.
    /// </summary>
    public partial class HomePageViewModel : BaseViewModel
    {
        private readonly ISshService _sshService;
        private readonly ILocalFileService _localFileService;
        private readonly ISessionManager _sessionManager;
        private readonly ITransferQueueService _transferQueueService;
        private readonly CredentialService _credentialService;
        private readonly ISettingsService _settingsService;
        private CancellationTokenSource? _connectCts;

        // Quick Connect Bar
        [ObservableProperty]
        private string _connectHost = "";

        [ObservableProperty]
        private int _connectPort = 22;

        [ObservableProperty]
        private string _connectUsername = "";

        [ObservableProperty]
        private string _connectPassword = "";

        [ObservableProperty]
        private ProtocolType _connectProtocol = ProtocolType.SFTP;

        [ObservableProperty]
        private AuthenticationType _connectAuthType = AuthenticationType.Password;

        [ObservableProperty]
        private string _connectKeyPath = "";

        [ObservableProperty]
        private string _connectPassphrase = "";

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _connectionStatus = "未连接";

        // Panel ViewModels
        [ObservableProperty]
        private FileBrowserViewModel _localPanel;

        [ObservableProperty]
        private FileBrowserViewModel _remotePanel;

        // Quick Connect suggestions
        [ObservableProperty]
        private ConnectionConfig? _selectedQuickConnect;

        public HomePageViewModel(
            ISshService sshService,
            ILocalFileService localFileService,
            ISessionManager sessionManager,
            ITransferQueueService transferQueueService,
            CredentialService credentialService,
            ISettingsService settingsService)
        {
            _sshService = sshService;
            _localFileService = localFileService;
            _sessionManager = sessionManager;
            _transferQueueService = transferQueueService;
            _credentialService = credentialService;
            _settingsService = settingsService;

            // Apply saved settings
            var s = _settingsService.Settings;

            _localPanel = new FileBrowserViewModel(localFileService) { ShowHiddenFiles = s.ShowHiddenFiles };
            _remotePanel = new FileBrowserViewModel(sshService) { ShowHiddenFiles = s.ShowHiddenFiles };

            _sshService.ConnectionStateChanged += (s, e) =>
            {
                IsConnected = e.IsConnected;
                ConnectionStatus = e.IsConnected
                    ? $"已连接: {_sshService.CurrentHost}"
                    : "未连接";
            };

            // Subscribe to resume requests to re-trigger transfers
            _transferQueueService.ResumeRequested += OnResumeRequested;
        }

        /// <summary>
        /// Initializes the home page by loading local files.
        /// Restores the last used local path from settings if available.
        /// </summary>
        [RelayCommand]
        public async Task InitializeAsync()
        {
            var savedPath = _settingsService.Settings.LastLocalPath;
            var homeDir = _localFileService.GetHomeDirectory();
            var startPath = !string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath)
                ? savedPath
                : homeDir;
            await LocalPanel.NavigateToAsync(startPath);
        }

        /// <summary>
        /// Connects to a remote server using the quick connect bar.
        /// </summary>
        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(ConnectHost))
            {
                SetError("请输入主机地址");
                return;
            }

            IsLoading = true;
            ClearError();
            ConnectionStatus = "连接中...";

            _connectCts?.Cancel();
            _connectCts = new CancellationTokenSource();

            try
            {
                var config = new ConnectionConfig
                {
                    Host = ConnectHost,
                    Port = ConnectPort,
                    Username = ConnectUsername,
                    EncryptedPassword = ConnectPassword,
                    PrivateKeyPath = ConnectKeyPath,
                    EncryptedPassphrase = ConnectPassphrase,
                    Protocol = ConnectProtocol,
                    AuthType = ConnectAuthType,
                    RemoteDefaultPath = "/"
                };

                await _sshService.ConnectAsync(config, _connectCts.Token);

                // Navigate remote panel to default path
                await RemotePanel.NavigateToAsync(config.RemoteDefaultPath);

                ConnectionStatus = $"已连接: {ConnectHost}";
                StatusMessage = $"已连接到 {ConnectHost}";
            }
            catch (OperationCanceledException)
            {
                ConnectionStatus = "连接已取消";
            }
            finally
            {
                IsLoading = false;
            }

            // Check if connection failed (error stored in LastError)
            if (!_sshService.IsConnected)
            {
                var error = _sshService.LastError ?? "连接失败";
                SetError(error);
                ConnectionStatus = "连接失败";
            }
        }

        /// <summary>
        /// Connects using a saved connection configuration.
        /// </summary>
        [RelayCommand]
        private async Task ConnectFromConfigAsync(ConnectionConfig config)
        {
            ConnectHost = config.Host;
            ConnectPort = config.Port;
            ConnectUsername = config.Username;
            ConnectPassword = config.EncryptedPassword;
            ConnectProtocol = config.Protocol;

            IsLoading = true;
            ClearError();
            ConnectionStatus = "连接中...";

            _connectCts?.Cancel();
            _connectCts = new CancellationTokenSource();

            try
            {
                await _sshService.ConnectAsync(config, _connectCts.Token);
                await RemotePanel.NavigateToAsync(config.RemoteDefaultPath);

                // Update last connected
                await _sessionManager.UpdateLastConnectedAsync(config.Id);

                ConnectionStatus = $"已连接: {config.Host}";
                StatusMessage = $"已连接到 {config.Name} ({config.Host})";
            }
            catch (Exception ex)
            {
                SetError($"连接失败: {ex.Message}");
                ConnectionStatus = "连接失败";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Disconnects from the remote server.
        /// </summary>
        [RelayCommand]
        private async Task DisconnectAsync()
        {
            await _sshService.DisconnectAsync();
            RemotePanel.LocalFiles.Clear();
            RemotePanel.RemoteFiles.Clear();
            RemotePanel.CurrentPath = "";
            ConnectionStatus = "已断开";
            StatusMessage = "已断开连接";
        }

        /// <summary>
        /// Uploads the selected local file(s) to the remote panel's current directory.
        /// </summary>
        [RelayCommand]
        private async Task UploadSelectedAsync()
        {
            if (!IsConnected || LocalPanel.SelectedLocalItem == null) return;
            if (LocalPanel.SelectedLocalItem.IsDirectory && LocalPanel.SelectedLocalItem.Name == "..") return;

            var selectedItem = LocalPanel.SelectedLocalItem;
            var remotePath = RemotePanel.CurrentPath.TrimEnd('/') + "/" + selectedItem.Name;

            try
            {
                // Create task(s) for the transfer queue
                TransferTask? taskItem;
                if (selectedItem.IsDirectory)
                {
                    var dirSize = GetDirectorySize(selectedItem.FullPath);
                    _transferQueueService.EnqueueUpload(
                        "", ConnectHost,
                        selectedItem.FullPath, remotePath, dirSize);
                }
                else
                {
                    var fileSize = _localFileService.GetFileSize(selectedItem.FullPath);
                    _transferQueueService.EnqueueUpload(
                        "", ConnectHost,
                        selectedItem.FullPath, remotePath, fileSize);
                }

                taskItem = _transferQueueService.Tasks.LastOrDefault(t => t.Status == TransferStatus.Pending);

                // Set up CancellationToken for pause/cancel support
                var cts = new CancellationTokenSource();
                if (taskItem != null)
                {
                    taskItem.CancellationTokenSource = cts;
                }

                var progress = new Progress<TransferProgress>(p =>
                {
                    if (taskItem != null)
                    {
                        // Don't overwrite Paused/Cancelled status
                        if (taskItem.Status == TransferStatus.Pending)
                        {
                            taskItem.Status = TransferStatus.Transferring;
                            taskItem.StartTime ??= DateTime.Now;
                        }
                        if (taskItem.Status == TransferStatus.Transferring)
                        {
                            taskItem.TransferredSize = p.BytesTransferred;
                            taskItem.TotalSize = p.TotalBytes;
                            taskItem.Speed = p.SpeedBytesPerSecond;

                            if (!string.IsNullOrEmpty(p.CurrentFileName))
                                taskItem.CurrentFileName = p.CurrentFileName;

                            var pct = p.TotalBytes > 0
                                ? (double)p.BytesTransferred / p.TotalBytes * 100.0
                                : 0;
                            StatusMessage = $"上传中: {selectedItem.Name} ({pct:F1}%) - {FormatSpeed(p.SpeedBytesPerSecond)}";
                        }
                    }
                });

                try
                {
                    var pauseEvent = taskItem?.PauseEvent;
                    if (selectedItem.IsDirectory)
                    {
                        await _sshService.UploadDirectoryAsync(selectedItem.FullPath, remotePath, progress, cts.Token, pauseEvent);
                    }
                    else
                    {
                        await _sshService.UploadFileAsync(selectedItem.FullPath, remotePath, progress, cts.Token, pauseEvent);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (taskItem != null)
                    {
                        taskItem.Speed = 0;
                        if (taskItem.Status == TransferStatus.Paused)
                        {
                            StatusMessage = $"上传已暂停: {selectedItem.Name}";
                        }
                        else
                        {
                            taskItem.Status = TransferStatus.Cancelled;
                            taskItem.EndTime = DateTime.Now;
                            StatusMessage = $"上传已取消: {selectedItem.Name}";
                        }
                    }
                    return;
                }
                catch (Exception ex)
                {
                    if (taskItem != null)
                    {
                        taskItem.Status = TransferStatus.Failed;
                        taskItem.ErrorMessage = ex.Message;
                        taskItem.Speed = 0;
                        taskItem.EndTime = DateTime.Now;
                    }
                    SetError($"上传失败: {ex.Message}");
                    return;
                }

                if (taskItem != null)
                {
                    taskItem.Status = TransferStatus.Completed;
                    taskItem.TransferredSize = taskItem.TotalSize;
                    taskItem.Speed = 0;
                    taskItem.EndTime = DateTime.Now;
                }
                StatusMessage = $"上传完成: {selectedItem.Name}";
                await RemotePanel.RefreshAsync();
            }
            catch (Exception ex)
            {
                SetError($"上传失败: {ex.Message}");
            }
        }

        /// <summary>
        /// Downloads the selected remote file(s) to the local panel's current directory.
        /// </summary>
        [RelayCommand]
        private async Task DownloadSelectedAsync()
        {
            if (!IsConnected || RemotePanel.SelectedRemoteItem == null) return;

            var selectedItem = RemotePanel.SelectedRemoteItem;
            var localPath = Path.Combine(LocalPanel.CurrentPath, selectedItem.Name);

            try
            {
                long taskTotalSize = selectedItem.Size;
                if (selectedItem.IsDirectory)
                {
                    taskTotalSize = 0;
                    _transferQueueService.EnqueueDownload(
                        "", ConnectHost,
                        selectedItem.FullPath, localPath, taskTotalSize);

                    StatusMessage = $"开始下载目录: {selectedItem.Name}";
                }
                else
                {
                    _transferQueueService.EnqueueDownload(
                        "", ConnectHost,
                        selectedItem.FullPath, localPath, taskTotalSize);

                    StatusMessage = $"开始下载: {selectedItem.Name}";
                }

                // Update transfer task progress during download
                var taskItem = _transferQueueService.Tasks.LastOrDefault(t => t.FileName == selectedItem.Name && t.Status == TransferStatus.Pending);

                // Set up CancellationToken for pause/cancel support
                var cts = new CancellationTokenSource();
                if (taskItem != null)
                {
                    taskItem.CancellationTokenSource = cts;
                }

                var progress = new Progress<TransferProgress>(p =>
                {
                    if (taskItem != null)
                    {
                        // Don't overwrite Paused/Cancelled status
                        if (taskItem.Status == TransferStatus.Pending)
                        {
                            taskItem.Status = TransferStatus.Transferring;
                            taskItem.StartTime ??= DateTime.Now;
                        }
                        if (taskItem.Status == TransferStatus.Transferring)
                        {
                            taskItem.TransferredSize = p.BytesTransferred;
                            taskItem.TotalSize = p.TotalBytes;
                            taskItem.Speed = p.SpeedBytesPerSecond;

                            if (!string.IsNullOrEmpty(p.CurrentFileName))
                                taskItem.CurrentFileName = p.CurrentFileName;

                            var pct = p.TotalBytes > 0
                                ? (double)p.BytesTransferred / p.TotalBytes * 100.0
                                : 0;
                            StatusMessage = $"下载中: {selectedItem.Name} ({pct:F1}%) - {FormatSpeed(p.SpeedBytesPerSecond)}";
                        }
                    }
                });

                try
                {
                    var pauseEvent = taskItem?.PauseEvent;
                    if (selectedItem.IsDirectory)
                    {
                        await _sshService.DownloadDirectoryAsync(selectedItem.FullPath, localPath, progress, cts.Token, pauseEvent);
                    }
                    else
                    {
                        await _sshService.DownloadFileAsync(selectedItem.FullPath, localPath, progress, cts.Token, pauseEvent);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (taskItem != null)
                    {
                        taskItem.Speed = 0;
                        if (taskItem.Status == TransferStatus.Paused)
                        {
                            StatusMessage = $"下载已暂停: {selectedItem.Name}";
                        }
                        else
                        {
                            taskItem.Status = TransferStatus.Cancelled;
                            taskItem.EndTime = DateTime.Now;
                            StatusMessage = $"下载已取消: {selectedItem.Name}";
                        }
                    }
                    return;
                }
                catch (Exception ex)
                {
                    if (taskItem != null)
                    {
                        taskItem.Status = TransferStatus.Failed;
                        taskItem.ErrorMessage = ex.Message;
                        taskItem.Speed = 0;
                        taskItem.EndTime = DateTime.Now;
                    }
                    SetError($"下载失败: {ex.Message}");
                    return;
                }

                if (taskItem != null)
                {
                    taskItem.Status = TransferStatus.Completed;
                    taskItem.TransferredSize = taskItem.TotalSize > 0 ? taskItem.TotalSize : taskItem.TransferredSize;
                    taskItem.Speed = 0;
                    taskItem.EndTime = DateTime.Now;
                }
                StatusMessage = $"下载完成: {selectedItem.Name}";
                await LocalPanel.RefreshAsync();
            }
            catch (Exception ex)
            {
                SetError($"下载失败: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles resume requests from the transfer queue service.
        /// With stream-based pause, resume just sets the PauseEvent to unblock the transfer.
        /// Only if the SFTP connection is broken do we need to re-trigger the full transfer.
        /// </summary>
        private async void OnResumeRequested(object? sender, TransferTask task)
        {
            // With the new stream-based pause approach, ResumeTaskAsync in TransferQueueService
            // already calls task.PauseEvent.Set() which unblocks the transfer stream.
            // This handler is only needed for reconnect scenarios when the connection was lost.
            
            // If SFTP is still connected, the transfer resumes automatically via PauseEvent.
            // Only intervene if the connection was lost.
            if (_sshService.IsConnected)
            {
                // Connection is alive, PauseEvent.Set() was already called by ResumeTaskAsync.
                // The background transfer thread will unblock automatically.
                return;
            }

            try
            {
                // Reconnect needed (SFTP connection broke)
                ConnectionStatus = "重新连接中...";
                var configs = await _sessionManager.GetAllAsync();
                var matchingConfig = configs.FirstOrDefault(c => c.Host == ConnectHost);
                ConnectionConfig reconnectConfig;
                if (matchingConfig != null)
                {
                    reconnectConfig = matchingConfig;
                }
                else
                {
                    reconnectConfig = new ConnectionConfig
                    {
                        Host = ConnectHost,
                        Port = ConnectPort,
                        Username = ConnectUsername,
                        EncryptedPassword = ConnectPassword,
                        Protocol = ConnectProtocol,
                        AuthType = ConnectAuthType,
                        PrivateKeyPath = ConnectKeyPath,
                        EncryptedPassphrase = ConnectPassphrase
                    };
                }
                try
                {
                    await _sshService.ConnectAsync(reconnectConfig);
                }
                catch { }
                if (!_sshService.IsConnected)
                {
                    task.Status = TransferStatus.Failed;
                    task.ErrorMessage = "无法重新连接到服务器";
                    return;
                }
                ConnectionStatus = $"已连接: {ConnectHost}";

                // Connection restored, unblock the transfer
                task.PauseEvent.Set();
            }
            catch (Exception ex)
            {
                task.Status = TransferStatus.Failed;
                task.ErrorMessage = $"恢复失败: {ex.Message}";
                task.Speed = 0;
                task.EndTime = DateTime.Now;
            }
        }

        private static long GetDirectorySize(string path)
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);
                long size = 0;
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    size += file.Length;
                }
                return size;
            }
            catch
            {
                return 0;
            }
        }

        private static List<FileInfo> GetAllFiles(string path)
        {
            var dirInfo = new DirectoryInfo(path);
            return dirInfo.GetFiles("*", SearchOption.AllDirectories).ToList();
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "--";
            string[] suffixes = ["B/s", "KB/s", "MB/s", "GB/s"];
            int order = 0;
            double size = bytesPerSecond;
            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {suffixes[order]}";
        }
    }
}