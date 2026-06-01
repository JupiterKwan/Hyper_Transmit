using CommunityToolkit.Mvvm.ComponentModel;
using Hyper_Transmit.Services.Interfaces;
using System.Threading.Tasks;

namespace Hyper_Transmit.ViewModels
{
    /// <summary>
    /// ViewModel for the main application window.
    /// Manages global state, navigation, and status bar information.
    /// </summary>
    public partial class MainWindowViewModel : BaseViewModel
    {
        private readonly ISessionManager _sessionManager;
        private readonly ISettingsService _settingsService;
        private readonly ITransferQueueService _transferQueueService;

        /// <summary>
        /// Number of active SSH connections.
        /// </summary>
        [ObservableProperty]
        private int _activeConnectionCount;

        /// <summary>
        /// Number of tasks in the transfer queue.
        /// </summary>
        [ObservableProperty]
        private int _queueCount;

        /// <summary>
        /// Number of currently transferring tasks.
        /// </summary>
        [ObservableProperty]
        private int _activeTransferCount;

        /// <summary>
        /// Overall transfer speed display string.
        /// </summary>
        [ObservableProperty]
        private string _overallSpeedDisplay = "--";

        /// <summary>
        /// Whether a connection is currently active.
        /// </summary>
        [ObservableProperty]
        private bool _isConnected;

        /// <summary>
        /// The currently connected host name.
        /// </summary>
        [ObservableProperty]
        private string _connectedHost = "";

        public MainWindowViewModel(
            ISessionManager sessionManager,
            ISettingsService settingsService,
            ITransferQueueService transferQueueService)
        {
            _sessionManager = sessionManager;
            _settingsService = settingsService;
            _transferQueueService = transferQueueService;

            _transferQueueService.QueueChanged += (s, e) =>
            {
                QueueCount = _transferQueueService.Tasks.Count;
            };

            _transferQueueService.ProgressChanged += (s, e) =>
            {
                UpdateTransferStats();
            };

            _transferQueueService.TaskCompleted += (s, e) =>
            {
                UpdateTransferStats();
            };
        }

        /// <summary>
        /// Initializes the ViewModel. Call after construction.
        /// </summary>
        public async Task InitializeAsync()
        {
            await _settingsService.LoadAsync();
            StatusMessage = "就绪";
        }

        private void UpdateTransferStats()
        {
            ActiveTransferCount = _transferQueueService.ActiveTransferCount;
            var speed = _transferQueueService.OverallSpeed;
            OverallSpeedDisplay = FormatSpeed(speed);
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