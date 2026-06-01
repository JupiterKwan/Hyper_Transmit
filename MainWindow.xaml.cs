using Hyper_Transmit.Models;
using Hyper_Transmit.Models.Enums;
using Hyper_Transmit.Services.Interfaces;
using Hyper_Transmit.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace Hyper_Transmit
{
    /// <summary>
    /// Main application window with NavigationView, Status Bar, and System Tray.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly DispatcherTimer _statusBarTimer;
        private readonly ITransferQueueService _transferQueueService;
        private readonly ISettingsService _settingsService;
        private readonly ObservableCollection<NotificationItem> _notifications = new();

        // ===== Win32 Interop =====
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const int SW_HIDE = 0;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA pnid);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        // Constants
        private const int GWLP_WNDPROC = -4;
        private const uint WM_TRAYICON = 0x0400 + 1; // WM_USER + 1
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;
        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint NIF_INFO = 0x00000010;
        private const int IDI_APPLICATION = 32512;
        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        // Tray icon state
        private IntPtr _hWnd;
        private IntPtr _oldWndProc;
        private NOTIFYICONDATA _trayIcon;
        private bool _trayIconCreated;
        private bool _isHiddenToTray;
        private WndProcDelegate? _wndProcDelegate; // prevent GC

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = App.Services.GetRequiredService<MainWindowViewModel>();
            _transferQueueService = App.Services.GetRequiredService<ITransferQueueService>();
            _settingsService = App.Services.GetRequiredService<ISettingsService>();
            RootFrame = ContentFrame;

            // Load settings and apply
            var mainDispatcher = DispatcherQueue.GetForCurrentThread();
            _ = _settingsService.LoadAsync().ContinueWith(_ =>
            {
                mainDispatcher?.TryEnqueue(() =>
                {
                    try
                    {
                        var settings = _settingsService.Settings;
                        // Apply theme to main content
                        if (Content is FrameworkElement root)
                            root.RequestedTheme = settings.Theme;
                        // Apply MaxConcurrentTransfers
                        _transferQueueService.MaxConcurrentTransfers = settings.MaxConcurrentTransfers;
                    }
                    catch { }
                });
            });

            // Minimize instead of close if setting is enabled
            this.AppWindow.Closing += (s, e) =>
            {
                try
                {
                    var settings = _settingsService.Settings;
                    if (settings.MinimizeToTray)
                    {
                        e.Cancel = true;
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                        ShowWindow(hwnd, SW_MINIMIZE);
                    }
                }
                catch { }
            };

            // Navigate to HomePage on startup
            ContentFrame.Navigate(typeof(HomePage));

            // Initialize the ViewModel
            _ = _viewModel.InitializeAsync();

            // Set title bar
            Title = "Hyper Transmit";

            // Set window icon from .ico file
            try
            {
                var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "hypertransmit.ico");
                if (!System.IO.File.Exists(iconPath))
                {
                    // Fallback: look in project root (dev environment)
                    iconPath = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
                        "..", "..", "..", "hypertransmit.ico");
                }
                if (System.IO.File.Exists(iconPath))
                {
                    this.AppWindow.SetIcon(iconPath);
                }
            }
            catch { }

            // Configure title bar colors to match theme
            UpdateTitleBarTheme();

            // Notification overlay binding
            NotificationOverlay.ItemsSource = _notifications;

            // Status bar update timer
            _statusBarTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusBarTimer.Tick += StatusBarTimer_Tick;
            _statusBarTimer.Start();

            // Initialize system tray icon after window is activated
            this.Activated += (s, e) =>
            {
                if (!_trayIconCreated)
                {
                    InitializeTrayIcon();
                }
            };
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

                // Subclass the window procedure to receive tray icon messages
                _wndProcDelegate = new WndProcDelegate(WndProc);
                _oldWndProc = SetWindowLongPtr(_hWnd, GWLP_WNDPROC,
                    System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

                // Load the application icon from the .ico file
                var appDir = AppContext.BaseDirectory;
                var icoPath = System.IO.Path.Combine(appDir, "hypertransmit.ico");
                IntPtr hIcon;
                if (System.IO.File.Exists(icoPath))
                {
                    hIcon = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                }
                else
                {
                    hIcon = LoadIcon(GetModuleHandle(null), (IntPtr)IDI_APPLICATION);
                }

                // Create the tray icon
                _trayIcon = new NOTIFYICONDATA
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hWnd,
                    uID = 1,
                    uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                    uCallbackMessage = WM_TRAYICON,
                    hIcon = hIcon,
                    szTip = "Hyper Transmit"
                };

                _trayIconCreated = Shell_NotifyIcon(NIM_ADD, ref _trayIcon);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize tray icon: {ex.Message}");
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAYICON)
            {
                var mouseMsg = (uint)lParam.ToInt32();
                if (mouseMsg == WM_LBUTTONDBLCLK)
                {
                    // Double-click: restore window
                    ShowWindow(_hWnd, SW_RESTORE);
                    SetForegroundWindow(_hWnd);
                    _isHiddenToTray = false;
                }
                else if (mouseMsg == WM_RBUTTONDOWN)
                {
                    // Right-click: show context menu
                    ShowTrayContextMenu();
                }
            }
            else if (msg == 0x0010) // WM_CLOSE
            {
                // If hidden to tray, prevent actual close
                if (_isHiddenToTray)
                {
                    return IntPtr.Zero;
                }
            }

            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        private void ShowTrayContextMenu()
        {
            try
            {
                var flyout = new MenuFlyout();
                var showItem = new MenuFlyoutItem { Text = "显示窗口" };
                showItem.Click += (s, e) =>
                {
                    ShowWindow(_hWnd, SW_RESTORE);
                    SetForegroundWindow(_hWnd);
                    _isHiddenToTray = false;
                };
                flyout.Items.Add(showItem);

                var exitItem = new MenuFlyoutItem { Text = "退出" };
                exitItem.Click += (s, e) =>
                {
                    // Remove tray icon before exit
                    if (_trayIconCreated)
                    {
                        Shell_NotifyIcon(NIM_DELETE, ref _trayIcon);
                        _trayIconCreated = false;
                    }
                    Application.Current.Exit();
                };
                flyout.Items.Add(exitItem);

                // Position at cursor
                // MenuFlyout needs an anchor, but for tray we need to show at cursor position
                // Use a temporary approach with a popup
                var point = Windows.UI.Core.CoreWindow.GetForCurrentThread().PointerPosition;
                // For tray icon context menus, we need to use the standard approach
                // Since WinUI 3 MenuFlyout requires an anchor, we'll use a simple approach
                // Show it at a temporary element near the bottom-right
                var tempBorder = new Border { Width = 1, Height = 1 };
                Content = Content; // Ensure content is loaded
                var root = Content as FrameworkElement;
                if (root != null)
                {
                    flyout.ShowAt(root);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show tray context menu: {ex.Message}");
            }
        }

        private void RemoveTrayIcon()
        {
            if (_trayIconCreated)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _trayIcon);
                _trayIconCreated = false;
            }
        }

        /// <summary>
        /// Reference to the content frame for pages to use.
        /// </summary>
        public Frame RootFrame { get; }
        public NavigationView NavView => MainNavView;

        /// <summary>
        /// Gets the MainWindowViewModel for pages to bind to status bar.
        /// </summary>
        public MainWindowViewModel ViewModel => _viewModel;

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem == null) return;

            Type? pageType = null;

            // Handle Settings item (gear icon)
            if (args.SelectedItem == sender.SettingsItem)
            {
                pageType = typeof(SettingsPage);
            }
            else if (args.SelectedItem is NavigationViewItem selectedItem)
            {
                var tag = selectedItem.Tag as string;
                pageType = tag switch
                {
                    "HomePage" => typeof(HomePage),
                    "ConnectionManagerPage" => typeof(ConnectionManagerPage),
                    "TransferQueuePage" => typeof(TransferQueuePage),
                    _ => null
                };
            }

            if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }

        /// <summary>
        /// Updates the status bar text.
        /// </summary>
        public void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }

        /// <summary>
        /// Updates the connection status in the status bar.
        /// </summary>
        public void UpdateConnectionStatus(string status)
        {
            // Connection status is now shown in the main status text
            // No separate element needed
        }

        /// <summary>
        /// Shows a floating notification in the top-right overlay.
        /// </summary>
        public void ShowNotification(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational, int durationMs = 3000)
        {
            var item = new NotificationItem { Title = title, Message = message, Severity = severity, Opacity = 1.0 };
            _notifications.Add(item);

            // Auto-remove after duration
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _notifications.Remove(item);
            };
            timer.Start();
        }

        /// <summary>
        /// Model for notification items in the overlay.
        /// </summary>
        public class NotificationItem
        {
            public string Title { get; set; } = "";
            public string Message { get; set; } = "";
            public InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;
            public double Opacity { get; set; } = 1.0;
        }

        /// <summary>
        /// Notifies the cached HomePage to apply new settings.
        /// Called from SettingsPage after save.
        /// </summary>
        public void NotifyHomePageSettingsChanged(AppSettings settings)
        {
            try
            {
                // Update title bar to match theme
                UpdateTitleBarTheme();

                if (_cachedHomePage != null)
                {
                    _cachedHomePage.ApplySettings(settings);
                }
            }
            catch { }
        }

        public void UpdateTitleBarTheme()
        {
            try
            {
                var settings = _settingsService.Settings;
                var titleBar = AppWindow.TitleBar;
                if (titleBar == null) return;

                // Determine effective theme
                bool isDark = settings.Theme == Microsoft.UI.Xaml.ElementTheme.Dark ||
                    (settings.Theme == Microsoft.UI.Xaml.ElementTheme.Default &&
                     Application.Current.RequestedTheme == ApplicationTheme.Dark);

                if (isDark)
                {
                    titleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 32, 32, 32);
                    titleBar.ForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 32, 32, 32);
                    titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 50, 50, 50);
                    titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 70, 70, 70);
                    titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 32, 32, 32);
                    titleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(255, 128, 128, 128);
                    titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 32, 32, 32);
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 128, 128, 128);
                }
                else
                {
                    titleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 243, 243, 243);
                    titleBar.ForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                    titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 243, 243, 243);
                    titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 220, 220, 220);
                    titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 200, 200, 200);
                    titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                    titleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 243, 243, 243);
                    titleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(255, 150, 150, 150);
                    titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 243, 243, 243);
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 150, 150, 150);
                }
            }
            catch { }
        }

        private HomePage? _cachedHomePage;

        /// <summary>
        /// Sets the cached HomePage reference for settings updates.
        /// Called by HomePage when it loads.
        /// </summary>
        public void SetCachedHomePage(HomePage page)
        {
            _cachedHomePage = page;
        }

        /// <summary>
        /// Updates transfer queue info in the status bar.
        /// </summary>
        public void UpdateStatusBar(
            int queueCount, int uploadingCount, int downloadingCount,
            int completedCount, long uploadBytes, long downloadBytes,
            double currentSpeed)
        {
            SpeedText.Text = currentSpeed > 0 ? FormatSpeed(currentSpeed) : "--";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < suffixes.Length - 1) { order++; size /= 1024; }
            return $"{size:0.##} {suffixes[order]}";
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "--";
            string[] suffixes = ["B/s", "KB/s", "MB/s", "GB/s"];
            int order = 0;
            double size = bytesPerSecond;
            while (size >= 1024 && order < suffixes.Length - 1) { order++; size /= 1024; }
            return $"{size:0.##} {suffixes[order]}";
        }

        /// <summary>
        /// Timer tick handler that polls TransferQueueService and updates status bar.
        /// </summary>
        private void StatusBarTimer_Tick(object? sender, object e)
        {
            var tasks = _transferQueueService.Tasks;

            int queueCount = tasks.Count(t =>
                t.Status == TransferStatus.Pending ||
                t.Status == TransferStatus.Transferring ||
                t.Status == TransferStatus.Paused);
            int uploadingCount = tasks.Count(t =>
                t.Status == TransferStatus.Transferring &&
                t.Direction == TransferDirection.Upload);
            int downloadingCount = tasks.Count(t =>
                t.Status == TransferStatus.Transferring &&
                t.Direction == TransferDirection.Download);
            int completedCount = tasks.Count(t =>
                t.Status == TransferStatus.Completed);

            long uploadBytes = tasks
                .Where(t => t.Direction == TransferDirection.Upload &&
                            (t.Status == TransferStatus.Completed ||
                             t.Status == TransferStatus.Transferring))
                .Sum(t => t.TransferredSize);
            long downloadBytes = tasks
                .Where(t => t.Direction == TransferDirection.Download &&
                            (t.Status == TransferStatus.Completed ||
                             t.Status == TransferStatus.Transferring))
                .Sum(t => t.TransferredSize);

            double currentSpeed = tasks
                .Where(t => t.Status == TransferStatus.Transferring)
                .Sum(t => t.Speed);

            UpdateStatusBar(queueCount, uploadingCount, downloadingCount,
                completedCount, uploadBytes, downloadBytes, currentSpeed);
        }
    }
}
