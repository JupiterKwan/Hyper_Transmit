using Hyper_Transmit.Models;
using Hyper_Transmit.Models.Enums;
using Hyper_Transmit.Services;
using Hyper_Transmit.Services.Interfaces;
using Hyper_Transmit.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Hyper_Transmit
{
    /// <summary>
    /// Home page with dual-panel file browser. Right panel shows connect form when not connected.
    /// </summary>
    public sealed partial class HomePage : Page
    {
        private readonly HomePageViewModel _viewModel;
        private readonly ILocalFileService _localFileService;
        private readonly ISshService _sshService;
        private readonly ITransferQueueService _transferQueueService;

        // Clipboard for copy/cut operations
        private static string? _clipboardPath;
        private static bool _clipboardIsCut;

        public HomePage()
        {
            InitializeComponent();

            _viewModel = App.Services.GetRequiredService<HomePageViewModel>();
            _localFileService = App.Services.GetRequiredService<ILocalFileService>();
            _sshService = App.Services.GetRequiredService<ISshService>();
            _transferQueueService = App.Services.GetRequiredService<ITransferQueueService>();

            DataContext = _viewModel;

            Loaded += HomePage_Loaded;
        }

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            // Apply settings that may not have been ready during ViewModel construction
            try
            {
                var settingsService = App.Services.GetRequiredService<ISettingsService>();
                var s = settingsService.Settings;
                _viewModel.LocalPanel.ShowHiddenFiles = s.ShowHiddenFiles;
                _viewModel.RemotePanel.ShowHiddenFiles = s.ShowHiddenFiles;
            }
            catch { }

            // Only initialize if the local panel hasn't been navigated yet
            // This preserves the current path when switching between tabs
            if (string.IsNullOrEmpty(_viewModel.LocalPanel.CurrentPath))
            {
                await _viewModel.InitializeAsync();
            }
            RefreshLocalPanel();

            // Register with MainWindow for settings updates
            if (App.MainWindow is MainWindow mw)
            {
                mw.SetCachedHomePage(this);
            }

            // Check for pending connection from ConnectionManager (auto-connect)
            var pendingConfig = ConnectionManagerPage.ConsumePendingConfig();
            if (pendingConfig != null)
            {
                // Pre-fill the connect form
                ConnectHostBox.Text = pendingConfig.Host;
                ConnectPortBox.Value = pendingConfig.Port;
                ConnectUsernameBox.Text = pendingConfig.Username;
                ConnectProtocolCombo.SelectedIndex = (int)pendingConfig.Protocol;
                ConnectAuthCombo.SelectedIndex = (int)pendingConfig.AuthType;
                UpdateAuthFieldsVisibility(pendingConfig.AuthType);

                // If password is encrypted and stored, auto-connect using the config directly
                if (!string.IsNullOrEmpty(pendingConfig.EncryptedPassword) || 
                    !string.IsNullOrEmpty(pendingConfig.PrivateKeyPath))
                {
                    // Pre-fill form for visual feedback
                    ConnectPasswordBox.Password = "••••••••";
                    
                    // Use ConnectFromConfigAsync which passes config directly to SshService
                    // (SshService handles decryption internally)
                    ShowNotification("正在一键连接...", InfoBarSeverity.Informational);
                    await _viewModel.ConnectFromConfigCommand.ExecuteAsync(pendingConfig);

                    if (_sshService.IsConnected)
                    {
                        ShowConnectedState();
                        UpdateMainWindowStatus($"已连接: {pendingConfig.Host}");
                    }
                    else
                    {
                        ShowConnectError(_sshService.LastError ?? "连接失败");
                    }
                }
                else
                {
                    // No password stored - ask user to enter
                    if (pendingConfig.AuthType == AuthenticationType.Password)
                        ConnectPasswordBox.Focus(FocusState.Programmatic);
                    else
                        ConnectKeyPathBox.Focus(FocusState.Programmatic);
                }
            }
            else if (_sshService.IsConnected)
            {
                ShowConnectedState();
            }
        }

        private void ConnectAuthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConnectAuthCombo == null) return;
            UpdateAuthFieldsVisibility((AuthenticationType)ConnectAuthCombo.SelectedIndex);
        }

        private void UpdateAuthFieldsVisibility(AuthenticationType authType)
        {
            if (PasswordSection == null || KeySection == null || PassphraseSection == null) return;

            switch (authType)
            {
                case AuthenticationType.Password:
                    PasswordSection.Visibility = Visibility.Visible;
                    KeySection.Visibility = Visibility.Collapsed;
                    PassphraseSection.Visibility = Visibility.Collapsed;
                    break;
                case AuthenticationType.PrivateKey:
                    PasswordSection.Visibility = Visibility.Collapsed;
                    KeySection.Visibility = Visibility.Visible;
                    PassphraseSection.Visibility = Visibility.Visible;
                    break;
                case AuthenticationType.KeyAndPassword:
                    PasswordSection.Visibility = Visibility.Visible;
                    KeySection.Visibility = Visibility.Visible;
                    PassphraseSection.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void ConnectProtocolCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Auto-set default port based on protocol
            if (ConnectPortBox == null) return;
            var protocol = (ProtocolType)ConnectProtocolCombo.SelectedIndex;
            if (ConnectPortBox.Value == 22 && (protocol == ProtocolType.FTP || protocol == ProtocolType.FTPS))
            {
                ConnectPortBox.Value = 21;
            }
            else if (ConnectPortBox.Value == 21 && (protocol == ProtocolType.SFTP || protocol == ProtocolType.SCP))
            {
                ConnectPortBox.Value = 22;
            }
        }

        private async void ConnectBrowseKeyButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".pem");
            picker.FileTypeFilter.Add(".ppk");
            picker.FileTypeFilter.Add(".key");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                ConnectKeyPathBox.Text = file.Path;
            }
        }

        #region Connect Form

        private async void ConnectSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            await DoConnect();
        }

        private async void ConnectPasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await DoConnect();
            }
        }

        private async Task DoConnect()
        {
            var host = ConnectHostBox.Text?.Trim();
            if (string.IsNullOrEmpty(host))
            {
                ShowConnectError("请输入主机地址");
                return;
            }

            var username = ConnectUsernameBox.Text?.Trim();
            if (string.IsNullOrEmpty(username))
            {
                ShowConnectError("请输入用户名");
                return;
            }

            ConnectSubmitButton.IsEnabled = false;
            ConnectSubmitButton.Content = "连接中...";
            ConnectErrorText.Visibility = Visibility.Collapsed;

            try
            {
                _viewModel.ConnectHost = host;
                _viewModel.ConnectPort = (int)ConnectPortBox.Value;
                _viewModel.ConnectUsername = username;
                _viewModel.ConnectPassword = ConnectPasswordBox.Password;
                _viewModel.ConnectProtocol = ConnectProtocolCombo.SelectedIndex switch
                {
                    0 => ProtocolType.SFTP,
                    1 => ProtocolType.SCP,
                    2 => ProtocolType.FTP,
                    3 => ProtocolType.FTPS,
                    _ => ProtocolType.SFTP
                };
                _viewModel.ConnectAuthType = (AuthenticationType)ConnectAuthCombo.SelectedIndex;
                _viewModel.ConnectKeyPath = ConnectKeyPathBox.Text?.Trim() ?? "";
                _viewModel.ConnectPassphrase = ConnectPassphraseBox.Password;

                await _viewModel.ConnectCommand.ExecuteAsync(null);

                if (_sshService.IsConnected)
                {
                    ShowConnectedState();
                    UpdateMainWindowStatus($"已连接: {host}");
                }
                else
                {
                    ShowConnectError(_sshService.LastError ?? "连接失败");
                }
            }
            catch (Exception ex)
            {
                ShowConnectError(ex.Message);
            }
            finally
            {
                ConnectSubmitButton.IsEnabled = true;
                ConnectSubmitButton.Content = "连接";
            }
        }

        private void ShowConnectError(string message)
        {
            ConnectErrorText.Text = message;
            ConnectErrorText.Visibility = Visibility.Visible;
        }

        #endregion

        #region UI State

        private void ShowConnectedState()
        {
            // Hide connect form, show file browser
            ConnectPanel.Visibility = Visibility.Collapsed;
            RemotePathBar.Visibility = Visibility.Visible;
            RemoteFileList.Visibility = Visibility.Visible;
            DisconnectOverlay.Visibility = Visibility.Visible;

            RemotePathBox.Text = _viewModel.RemotePanel.CurrentPath;
            RefreshRemotePanel();
        }

        private void ShowDisconnectedState()
        {
            // Show connect form, hide file browser
            ConnectPanel.Visibility = Visibility.Visible;
            RemotePathBar.Visibility = Visibility.Collapsed;
            RemoteFileList.Visibility = Visibility.Collapsed;
            DisconnectOverlay.Visibility = Visibility.Collapsed;

            RemoteFileList.ItemsSource = null;
            RemotePathBox.Text = "";
            RemoteStatusText.Text = "未连接";
                UploadButton.IsEnabled = false;
                DownloadButton.IsEnabled = false;
        }

        private void SetConnectingState(bool connecting)
        {
            ConnectSubmitButton.IsEnabled = !connecting;
        }

        #endregion

        #region Local Panel

        private async void LocalUpButton_Click(object sender, RoutedEventArgs e)
        {
            var currentPath = _viewModel.LocalPanel.CurrentPath;
            if (string.IsNullOrEmpty(currentPath)) return;

            var parent = _localFileService.GetParentPath(currentPath);
            if (parent != null)
            {
                await _viewModel.LocalPanel.NavigateToAsync(parent);
                LocalPathBox.Text = _viewModel.LocalPanel.CurrentPath;
                RefreshLocalPanel();
                SaveLocalPath();
            }
        }

        private async void LocalRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.LocalPanel.RefreshAsync();
            RefreshLocalPanel();
        }

        private async void LocalPathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var path = LocalPathBox.Text?.Trim();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    await _viewModel.LocalPanel.NavigateToAsync(path);
                    RefreshLocalPanel();
                    SaveLocalPath();
                }
            }
        }

        private async void LocalFileList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (LocalFileList.SelectedItem is LocalFileInfo item)
            {
                if (item.Name == "..")
                {
                    var parent = _localFileService.GetParentPath(_viewModel.LocalPanel.CurrentPath);
                    if (parent != null)
                    {
                        await _viewModel.LocalPanel.NavigateToAsync(parent);
                    }
                }
                else if (item.IsDirectory)
                {
                    await _viewModel.LocalPanel.NavigateToAsync(item.FullPath);
                }
                LocalPathBox.Text = _viewModel.LocalPanel.CurrentPath;
                RefreshLocalPanel();
                SaveLocalPath();
            }
        }

        private void LocalFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var hasSelection = LocalFileList.SelectedItem is LocalFileInfo item && item.Name != "..";
            UploadButton.IsEnabled = hasSelection && _sshService.IsConnected;
            _viewModel.LocalPanel.SelectedLocalItem = LocalFileList.SelectedItem as LocalFileInfo;
            UpdateLocalStatus();
        }

        private void RefreshLocalPanel()
        {
            LocalFileList.ItemsSource = _viewModel.LocalPanel.LocalFiles;
            LocalPathBox.Text = _viewModel.LocalPanel.CurrentPath;
            UpdateLocalStatus();
        }

        private void UpdateLocalStatus()
        {
            var count = _viewModel.LocalPanel.LocalFiles.Count;
            LocalStatusText.Text = $"{count} 项";
        }

        private async void LocalDrivesButton_Click(object sender, RoutedEventArgs e)
        {
            var flyout = new MenuFlyout();
            var drives = System.IO.DriveInfo.GetDrives().Where(d => d.IsReady);
            foreach (var drive in drives)
            {
                var item = new MenuFlyoutItem();
                var label = string.IsNullOrEmpty(drive.VolumeLabel)
                    ? drive.Name
                    : $"{drive.Name} ({drive.VolumeLabel})";
                var totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
                var usedGb = totalGb - drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                item.Text = $"{label}  [{usedGb:F0}GB / {totalGb:F0}GB]";
                var driveRoot = drive.RootDirectory.FullName;
                item.Click += async (s, args) =>
                {
                    await _viewModel.LocalPanel.NavigateToAsync(driveRoot);
                    RefreshLocalPanel();
                    SaveLocalPath();
                };
                flyout.Items.Add(item);
            }
            flyout.ShowAt(LocalDrivesButton);
        }

        private void SaveLocalPath()
        {
            try
            {
                var settingsService = App.Services.GetRequiredService<ISettingsService>();
                settingsService.Settings.LastLocalPath = _viewModel.LocalPanel.CurrentPath;
                _ = settingsService.SaveAsync();
            }
            catch { }
        }

        #endregion

        #region Remote Panel

        private async void RemoteUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_sshService.IsConnected) return;

            await _viewModel.RemotePanel.NavigateUpAsync();
            RemotePathBox.Text = _viewModel.RemotePanel.CurrentPath;
            RefreshRemotePanel();
        }

        private async void RemoteRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_sshService.IsConnected) return;

            await _viewModel.RemotePanel.RefreshAsync();
            RefreshRemotePanel();
        }

        private async void RemotePathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter && _sshService.IsConnected)
            {
                var path = RemotePathBox.Text?.Trim();
                if (!string.IsNullOrEmpty(path))
                {
                    await _viewModel.RemotePanel.NavigateToAsync(path);
                    RefreshRemotePanel();
                }
            }
        }

        private async void RemoteFileList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (RemoteFileList.SelectedItem is RemoteFileInfo item && item.IsDirectory)
            {
                await _viewModel.RemotePanel.NavigateToAsync(item.FullPath);
                RemotePathBox.Text = _viewModel.RemotePanel.CurrentPath;
                RefreshRemotePanel();
            }
        }

        private void RemoteFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DownloadButton.IsEnabled = RemoteFileList.SelectedItem is RemoteFileInfo;
            _viewModel.RemotePanel.SelectedRemoteItem = RemoteFileList.SelectedItem as RemoteFileInfo;
            UpdateRemoteStatus();
        }

        private void RefreshRemotePanel()
        {
            RemoteFileList.ItemsSource = _viewModel.RemotePanel.RemoteFiles;
            RemotePathBox.Text = _viewModel.RemotePanel.CurrentPath;
            UpdateRemoteStatus();
        }

        private void UpdateRemoteStatus()
        {
            var count = _viewModel.RemotePanel.RemoteFiles.Count;
            RemoteStatusText.Text = _sshService.IsConnected ? $"{count} 项" : "未连接";
        }

        #endregion

        #region Disconnect

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            await _sshService.DisconnectAsync();
            _viewModel.RemotePanel.RemoteFiles.Clear();
            _viewModel.RemotePanel.CurrentPath = "";
            ShowDisconnectedState();
            UpdateMainWindowStatus("已断开连接");
        }

        #endregion

        #region Transfer Buttons

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var name = _viewModel.LocalPanel?.SelectedLocalItem?.Name;
            ShowNotification($"开始上传: {name}", InfoBarSeverity.Informational);
            await _viewModel.UploadSelectedCommand.ExecuteAsync(null);
            RefreshRemotePanel();
            UpdateMainWindowStatus(_viewModel.StatusMessage);
            if (_viewModel.StatusMessage?.Contains("完成") == true)
            {
                ShowNotification($"上传完成: {name}", InfoBarSeverity.Success);
                PlaySoundIfEnabled();
                LogTransfer("上传", name ?? "", true);
            }
            else if (_viewModel.StatusMessage?.Contains("失败") == true || _viewModel.HasError)
            {
                ShowNotification($"上传失败: {name}", InfoBarSeverity.Error);
                LogTransfer("上传", name ?? "", false);
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = _viewModel.RemotePanel?.SelectedRemoteItem;
            if (selectedItem == null) return;
            var name = selectedItem.Name;
            var localPath = System.IO.Path.Combine(_viewModel.LocalPanel.CurrentPath, name);

            // Check if anything exists at the target path
            bool fileExists = System.IO.File.Exists(localPath);
            bool dirExists = System.IO.Directory.Exists(localPath);

            if (fileExists || dirExists)
            {
                string existingInfo = fileExists
                    ? $"文件 \"{name}\" ({FormatFileSize(new System.IO.FileInfo(localPath).Length)})"
                    : $"目录 \"{name}\"";

                var dialog = new ContentDialog
                {
                    Title = "目标已存在",
                    Content = $"本地已存在 {existingInfo}。\n\n选择\"覆盖\"将删除并重新下载。",
                    PrimaryButtonText = "覆盖",
                    SecondaryButtonText = "跳过",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Secondary,
                    XamlRoot = Content.XamlRoot
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Secondary)
                    return; // Skip
                if (result == ContentDialogResult.None)
                    return; // Cancel

                // Overwrite: clean up existing file or directory
                try
                {
                    if (dirExists)
                        System.IO.Directory.Delete(localPath, true);
                    else if (fileExists)
                        System.IO.File.Delete(localPath);
                }
                catch (Exception ex)
                {
                    ShowNotification($"无法删除已存在的文件: {ex.Message}", InfoBarSeverity.Error);
                    return;
                }
            }

            ShowNotification($"开始下载: {name}", InfoBarSeverity.Informational);
            await _viewModel.DownloadSelectedCommand.ExecuteAsync(null);
            RefreshLocalPanel();
            UpdateMainWindowStatus(_viewModel.StatusMessage);
            if (_viewModel.StatusMessage?.Contains("完成") == true)
            {
                ShowNotification($"下载完成: {name}", InfoBarSeverity.Success);
                PlaySoundIfEnabled();
                LogTransfer("下载", name ?? "", true);
            }
            else if (_viewModel.StatusMessage?.Contains("失败") == true || _viewModel.HasError)
            {
                ShowNotification($"下载失败: {name}", InfoBarSeverity.Error);
                LogTransfer("下载", name ?? "", false);
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < suffixes.Length - 1) { order++; size /= 1024; }
            return $"{size:0.##} {suffixes[order]}";
        }

        private void PlaySoundIfEnabled()
        {
            try
            {
                var settings = App.Services.GetRequiredService<ISettingsService>();
                if (settings.Settings.SoundOnComplete)
                {
                    // Use PlaySound Win32 API for reliable sound in unpackaged mode
                    PlayNotificationSound();
                }
            }
            catch { /* ignore sound errors */ }
        }

        [System.Runtime.InteropServices.DllImport("winmm.dll", SetLastError = true)]
        private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

        private const uint SND_ALIAS = 0x00010000;
        private const uint SND_ASYNC = 0x00000001;
        private const uint SND_FILENAME = 0x00020000;

        private static void PlayNotificationSound()
        {
            try
            {
                // Play the Windows default notification sound
                // SND_ALIAS | SND_ASYNC plays the SystemDefault sound asynchronously
                PlaySound("SystemDefault", IntPtr.Zero, SND_ALIAS | SND_ASYNC);
            }
            catch
            {
                // Fallback: try MediaPlayer
                try
                {
                    var player = new Windows.Media.Playback.MediaPlayer();
                    var source = Windows.Media.Core.MediaSource.CreateFromUri(
                        new Uri("ms-winsoundevent:Notification.Default"));
                    player.Source = source;
                    player.Play();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    timer.Tick += (s, e) => { timer.Stop(); player.Dispose(); };
                    timer.Start();
                }
                catch { }
            }
        }

        private void LogTransfer(string direction, string fileName, bool success)
        {
            try
            {
                var settings = App.Services.GetRequiredService<ISettingsService>();
                if (!settings.Settings.LogTransferHistory) return;

                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HyperTransmit", "history");
                Directory.CreateDirectory(logDir);
                var logFile = Path.Combine(logDir, "transfers.log");
                var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {direction} | {fileName} | {(success ? "成功" : "失败")}\n";
                File.AppendAllText(logFile, entry);
            }
            catch { /* ignore log errors */ }
        }

        #endregion

        #region Context Menus

        private void LocalFileList_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            try
            {
                // Find the data context from the tapped element
                var element = e.OriginalSource as FrameworkElement;
                while (element != null && element is not ListViewItem)
                    element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element) as FrameworkElement;

                if (element is ListViewItem lvi && lvi.Content is LocalFileInfo item)
                {
                    LocalFileList.SelectedItem = item;
                    if (LocalPasteMenuItem != null)
                        LocalPasteMenuItem.IsEnabled = _clipboardPath != null;
                    LocalContextMenu.ShowAt(lvi, new FlyoutShowOptions { Position = e.GetPosition(lvi) });
                }
                else
                {
                    // Right-clicked on empty area
                    if (LocalPasteMenuItem != null)
                        LocalPasteMenuItem.IsEnabled = _clipboardPath != null;
                    LocalContextMenu.ShowAt(LocalFileList, new FlyoutShowOptions { Position = e.GetPosition(LocalFileList) });
                }
            }
            catch { /* prevent crash */ }
            e.Handled = true;
        }

        private void RemoteFileList_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            try
            {
                var element = e.OriginalSource as FrameworkElement;
                while (element != null && element is not ListViewItem)
                    element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element) as FrameworkElement;

                if (element is ListViewItem lvi && lvi.Content is RemoteFileInfo item)
                {
                    RemoteFileList.SelectedItem = item;
                    RemoteContextMenu.ShowAt(lvi, new FlyoutShowOptions { Position = e.GetPosition(lvi) });
                }
                else
                {
                    RemoteContextMenu.ShowAt(RemoteFileList, new FlyoutShowOptions { Position = e.GetPosition(RemoteFileList) });
                }
            }
            catch { /* prevent crash */ }
            e.Handled = true;
        }

        private void LocalContextCopy_Click(object sender, RoutedEventArgs e)
        {
            if (LocalFileList.SelectedItem is LocalFileInfo item && item.Name != "..")
            {
                _clipboardPath = item.FullPath;
                _clipboardIsCut = false;
            }
        }

        private void LocalContextCut_Click(object sender, RoutedEventArgs e)
        {
            if (LocalFileList.SelectedItem is LocalFileInfo item && item.Name != "..")
            {
                _clipboardPath = item.FullPath;
                _clipboardIsCut = true;
            }
        }

        private async void LocalContextPaste_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_clipboardPath)) return;

            try
            {
                var sourceName = Path.GetFileName(_clipboardPath);
                var destPath = Path.Combine(_viewModel.LocalPanel.CurrentPath, sourceName);

                if (_clipboardPath == destPath) return;

                if (Directory.Exists(_clipboardPath))
                {
                    CopyDirectory(_clipboardPath, destPath);
                    if (_clipboardIsCut) Directory.Delete(_clipboardPath, true);
                }
                else if (File.Exists(_clipboardPath))
                {
                    File.Copy(_clipboardPath, destPath, overwrite: true);
                    if (_clipboardIsCut) File.Delete(_clipboardPath);
                }

                if (_clipboardIsCut) _clipboardPath = null;

                await _viewModel.LocalPanel.RefreshAsync();
                RefreshLocalPanel();
            }
            catch (Exception ex)
            {
                ShowError($"粘贴失败: {ex.Message}");
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
        }

        private async void LocalContextUpload_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.UploadSelectedCommand.ExecuteAsync(null);
            RefreshRemotePanel();
        }

        private async void LocalContextRename_Click(object sender, RoutedEventArgs e)
        {
            if (LocalFileList.SelectedItem is not LocalFileInfo item || item.Name == "..") return;
            var dialog = new ContentDialog
            {
                Title = "重命名",
                Content = new TextBox { Text = item.Name },
                PrimaryButtonText = "确定",
                SecondaryButtonText = "取消",
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var newName = ((TextBox)dialog.Content).Text;
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    var newPath = Path.Combine(Path.GetDirectoryName(item.FullPath)!, newName);
                    await _localFileService.RenameAsync(item.FullPath, newPath);
                    await _viewModel.LocalPanel.RefreshAsync();
                    RefreshLocalPanel();
                }
            }
        }

        private async void LocalContextDelete_Click(object sender, RoutedEventArgs e)
        {
            if (LocalFileList.SelectedItem is not LocalFileInfo item || item.Name == "..") return;
            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除 \"{item.Name}\" 吗？",
                PrimaryButtonText = "删除",
                SecondaryButtonText = "取消",
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (item.IsDirectory)
                    await _localFileService.DeleteDirectoryAsync(item.FullPath, true);
                else
                    await _localFileService.DeleteFileAsync(item.FullPath);
                await _viewModel.LocalPanel.RefreshAsync();
                RefreshLocalPanel();
            }
        }

        private async void LocalContextNewFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "新建文件夹",
                Content = new TextBox { PlaceholderText = "文件夹名称" },
                PrimaryButtonText = "创建",
                SecondaryButtonText = "取消",
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var folderName = ((TextBox)dialog.Content).Text;
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    var newPath = Path.Combine(_viewModel.LocalPanel.CurrentPath, folderName);
                    await _localFileService.CreateDirectoryAsync(newPath);
                    await _viewModel.LocalPanel.RefreshAsync();
                    RefreshLocalPanel();
                }
            }
        }

        private async void RemoteContextDownload_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.DownloadSelectedCommand.ExecuteAsync(null);
            RefreshLocalPanel();
        }

        private async void RemoteContextRename_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteFileList.SelectedItem is not RemoteFileInfo item) return;
            var dialog = new ContentDialog
            {
                Title = "重命名",
                Content = new TextBox { Text = item.Name },
                PrimaryButtonText = "确定",
                SecondaryButtonText = "取消",
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var newName = ((TextBox)dialog.Content).Text;
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    var parentPath = item.FullPath[..item.FullPath.LastIndexOf('/')];
                    var newPath = parentPath + "/" + newName;
                    await _sshService.RenameAsync(item.FullPath, newPath);
                    await _viewModel.RemotePanel.RefreshAsync();
                    RefreshRemotePanel();
                }
            }
        }

        private async void RemoteContextDelete_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteFileList.SelectedItem is not RemoteFileInfo item) return;
            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除 \"{item.Name}\" 吗？",
                PrimaryButtonText = "删除",
                SecondaryButtonText = "取消",
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (item.IsDirectory)
                    await _sshService.DeleteDirectoryAsync(item.FullPath, true);
                else
                    await _sshService.DeleteFileAsync(item.FullPath);
                await _viewModel.RemotePanel.RefreshAsync();
                RefreshRemotePanel();
            }
        }

        private async void RemoteContextPermissions_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteFileList.SelectedItem is not RemoteFileInfo item) return;
            var dialog = new ContentDialog
            {
                Title = $"权限: {item.Name}",
                Content = new TextBox { Text = item.PermissionsOctal.ToString(), PlaceholderText = "权限 (如 755)" },
                PrimaryButtonText = "应用",
                SecondaryButtonText = "取消",
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (int.TryParse(((TextBox)dialog.Content).Text, out int perms))
                {
                    await _sshService.SetPermissionsAsync(item.FullPath, perms);
                    await _viewModel.RemotePanel.RefreshAsync();
                    RefreshRemotePanel();
                }
            }
        }

        private async void RemoteContextNewFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "新建文件夹",
                Content = new TextBox { PlaceholderText = "文件夹名称" },
                PrimaryButtonText = "创建",
                SecondaryButtonText = "取消",
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var folderName = ((TextBox)dialog.Content).Text;
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    var newPath = _viewModel.RemotePanel.CurrentPath.TrimEnd('/') + "/" + folderName;
                    await _sshService.CreateDirectoryAsync(newPath);
                    await _viewModel.RemotePanel.RefreshAsync();
                    RefreshRemotePanel();
                }
            }
        }

        #endregion

        /// <summary>
        /// Applies settings changes in real-time. Called from SettingsPage after save.
        /// </summary>
        public void ApplySettings(Hyper_Transmit.Models.AppSettings settings)
        {
            // Apply hidden files filter
            _viewModel.LocalPanel.ShowHiddenFiles = settings.ShowHiddenFiles;
            _viewModel.RemotePanel.ShowHiddenFiles = settings.ShowHiddenFiles;

            // Refresh file lists to apply hidden files filter
            // This recreates the collections, so we need to rebind
            _ = _viewModel.LocalPanel.RefreshAsync().ContinueWith(_ =>
            {
                DispatcherQueue.TryEnqueue(() => RefreshLocalPanel());
            });

            if (_sshService.IsConnected)
            {
                _ = _viewModel.RemotePanel.RefreshAsync().ContinueWith(_ =>
                {
                    DispatcherQueue.TryEnqueue(() => RefreshRemotePanel());
                });
            }
        }

        #region UI Helpers

        private void ShowError(string message)
        {
            ShowNotification(message, InfoBarSeverity.Error);
        }

        private void ShowNotification(string message, InfoBarSeverity severity)
        {
            if (App.MainWindow is MainWindow mainWindow)
            {
                var title = severity switch
                {
                    InfoBarSeverity.Error => "错误",
                    InfoBarSeverity.Warning => "警告",
                    InfoBarSeverity.Success => "成功",
                    _ => "提示"
                };
                mainWindow.ShowNotification(title, message, severity);
            }
        }

        private void UpdateMainWindowStatus(string message)
        {
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.UpdateStatus(message);
                mainWindow.UpdateConnectionStatus(
                    _sshService.IsConnected ? $"已连接: {_sshService.CurrentHost}" : "未连接");
            }
        }

        public static string GetFileIcon(bool isDirectory, string extension)
        {
            if (isDirectory) return "\uE8B7";
            return extension?.ToLower() switch
            {
                ".txt" or ".log" or ".md" => "\uE8A5",
                ".cs" or ".js" or ".ts" or ".py" or ".java" => "\uE943",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" => "\uEB9F",
                ".mp3" or ".wav" or ".flac" => "\uE8B6",
                ".mp4" or ".avi" or ".mkv" => "\uE8B2",
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "\uE8AB",
                ".pdf" => "\uEA90",
                ".doc" or ".docx" => "\uE8A5",
                ".xls" or ".xlsx" => "\uE8A5",
                ".xml" or ".json" or ".yaml" or ".yml" => "\uE8A5",
                _ => "\uE8A5"
            };
        }

        public static string GetRemoteFileIcon(bool isDirectory)
        {
            return isDirectory ? "\uE8B7" : "\uE8A5";
        }

        public static string FormatDate(DateTime date)
        {
            // Use saved date format from settings if available
            try
            {
                var settings = App.Services.GetRequiredService<Hyper_Transmit.Services.Interfaces.ISettingsService>();
                return date.ToString(settings.Settings.DateFormat);
            }
            catch
            {
                return date.ToString("yyyy-MM-dd HH:mm");
            }
        }

        #endregion
    }
}