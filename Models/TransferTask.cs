using Hyper_Transmit.Models.Enums;
using Microsoft.UI.Dispatching;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Hyper_Transmit.Models
{
    /// <summary>
    /// Represents a single file transfer task in the queue.
    /// </summary>
    public class TransferTask : INotifyPropertyChanged
    {
        private static DispatcherQueue? _dispatcher;
        
        public static void SetDispatcher(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            if (_dispatcher != null && !_dispatcher.HasThreadAccess)
            {
                _dispatcher.TryEnqueue(() =>
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
            }
            else
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string FileName { get; set; } = "";

        public string SourcePath { get; set; } = "";

        public string DestinationPath { get; set; } = "";

        private long _totalSize;
        public long TotalSize { get => _totalSize; set { _totalSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(TotalSizeDisplay)); } }

        private long _transferredSize;
        public long TransferredSize { get => _transferredSize; set { _transferredSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); } }

        private TransferDirection _direction;
        public TransferDirection Direction { get => _direction; set { _direction = value; OnPropertyChanged(); OnPropertyChanged(nameof(DirectionDisplay)); } }

        private TransferStatus _status = TransferStatus.Pending;
        public TransferStatus Status { get => _status; set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); } }

        /// <summary>
        /// Current transfer speed in bytes per second.
        /// </summary>
        private double _speed;
        public double Speed { get => _speed; set { _speed = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedDisplay)); OnPropertyChanged(nameof(EtaDisplay)); } }

        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        private string _errorMessage = "";
        public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }

        private string _currentFileName = "";
        /// <summary>
        /// The name of the file currently being transferred (for directory transfers).
        /// </summary>
        public string CurrentFileName { get => _currentFileName; set { _currentFileName = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); } }

        public string ConnectionId { get; set; } = "";

        public string ConnectionName { get; set; } = "";

        /// <summary>
        /// CancellationTokenSource for this transfer task.
        /// </summary>
        public CancellationTokenSource? CancellationTokenSource { get; set; }

        /// <summary>
        /// ManualResetEvent used to pause/resume the transfer stream.
        /// When signaled (Set), transfer proceeds. When reset, transfer blocks.
        /// </summary>
        public ManualResetEventSlim PauseEvent { get; set; } = new ManualResetEventSlim(true);

        /// <summary>
        /// Progress percentage (0-100).
        /// </summary>
        public double ProgressPercent => TotalSize > 0
            ? Math.Min(100.0, (double)TransferredSize / TotalSize * 100)
            : 0;

        /// <summary>
        /// Estimated time remaining for the transfer.
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining => Speed > 0 && TotalSize > TransferredSize
            ? TimeSpan.FromSeconds((TotalSize - TransferredSize) / Speed)
            : null;

        /// <summary>
        /// Formatted speed string, e.g. "12.5 MB/s".
        /// </summary>
        public string SpeedDisplay
        {
            get
            {
                if (Speed <= 0) return "--";
                string[] suffixes = ["B/s", "KB/s", "MB/s", "GB/s"];
                int order = 0;
                double size = Speed;
                while (size >= 1024 && order < suffixes.Length - 1)
                {
                    order++;
                    size /= 1024;
                }
                return $"{size:0.##} {suffixes[order]}";
            }
        }

        /// <summary>
        /// Formatted ETA string.
        /// </summary>
        public string EtaDisplay
        {
            get
            {
                var eta = EstimatedTimeRemaining;
                if (eta == null) return "--";
                if (eta.Value.TotalHours >= 1)
                    return $"{(int)eta.Value.TotalHours}h {eta.Value.Minutes}m";
                if (eta.Value.TotalMinutes >= 1)
                    return $"{eta.Value.Minutes}m {eta.Value.Seconds}s";
                return $"{eta.Value.Seconds}s";
            }
        }

        /// <summary>
        /// Formatted total size string.
        /// </summary>
        public string TotalSizeDisplay
        {
            get
            {
                string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
                int order = 0;
                double size = TotalSize;
                while (size >= 1024 && order < suffixes.Length - 1)
                {
                    order++;
                    size /= 1024;
                }
                return $"{size:0.##} {suffixes[order]}";
            }
        }

        /// <summary>
        /// Direction display string.
        /// </summary>
        public string DirectionDisplay => Direction == TransferDirection.Upload ? "↑ Upload" : "↓ Download";

        /// <summary>
        /// Status display string with direction info when transferring.
        /// </summary>
        public string StatusDisplay => Status switch
        {
            TransferStatus.Pending => "等待中",
            TransferStatus.Transferring => Direction == TransferDirection.Upload
                ? (string.IsNullOrEmpty(CurrentFileName) ? "上传中" : $"上传中: {CurrentFileName}")
                : (string.IsNullOrEmpty(CurrentFileName) ? "下载中" : $"下载中: {CurrentFileName}"),
            TransferStatus.Paused => "已暂停",
            TransferStatus.Completed => "已完成",
            TransferStatus.Failed => "失败",
            TransferStatus.Cancelled => "已取消",
            _ => ""
        };
    }
}