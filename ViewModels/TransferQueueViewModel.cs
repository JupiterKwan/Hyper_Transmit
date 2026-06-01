using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyper_Transmit.Models;
using Hyper_Transmit.Services.Interfaces;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Hyper_Transmit.ViewModels
{
    /// <summary>
    /// ViewModel for the Transfer Queue page.
    /// </summary>
    public partial class TransferQueueViewModel : BaseViewModel
    {
        private readonly ITransferQueueService _transferQueueService;

        [ObservableProperty]
        private ObservableCollection<TransferTask> _tasks = new();

        [ObservableProperty]
        private TransferTask? _selectedTask;

        [ObservableProperty]
        private int _completedCount;

        [ObservableProperty]
        private int _pendingCount;

        [ObservableProperty]
        private int _failedCount;

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private string _overallSpeedDisplay = "--";

        public TransferQueueViewModel(ITransferQueueService transferQueueService)
        {
            _transferQueueService = transferQueueService;
            Tasks = _transferQueueService.Tasks;

            _transferQueueService.QueueChanged += (s, e) => UpdateStats();
            _transferQueueService.ProgressChanged += (s, e) => UpdateStats();
            _transferQueueService.TaskCompleted += (s, e) => UpdateStats();
        }

        [RelayCommand]
        private async Task PauseAllAsync()
        {
            await _transferQueueService.PauseAllAsync();
            UpdateStats();
        }

        [RelayCommand]
        private async Task ResumeAllAsync()
        {
            await _transferQueueService.ResumeAllAsync();
            _transferQueueService.StartProcessing();
            UpdateStats();
        }

        [RelayCommand]
        private async Task CancelAllAsync()
        {
            await _transferQueueService.CancelAllAsync();
            UpdateStats();
        }

        [RelayCommand]
        private void ClearCompleted()
        {
            _transferQueueService.ClearCompleted();
            UpdateStats();
        }

        [RelayCommand]
        private async Task PauseTaskAsync()
        {
            if (SelectedTask == null) return;
            await _transferQueueService.PauseTaskAsync(SelectedTask.Id);
            UpdateStats();
        }

        [RelayCommand]
        private async Task ResumeTaskAsync()
        {
            if (SelectedTask == null) return;
            await _transferQueueService.ResumeTaskAsync(SelectedTask.Id);
            _transferQueueService.StartProcessing();
            UpdateStats();
        }

        [RelayCommand]
        private async Task CancelTaskAsync()
        {
            if (SelectedTask == null) return;
            await _transferQueueService.CancelTaskAsync(SelectedTask.Id);
            UpdateStats();
        }

        [RelayCommand]
        private async Task RetryTaskAsync()
        {
            if (SelectedTask == null) return;
            await _transferQueueService.RetryTaskAsync(SelectedTask.Id);
            _transferQueueService.StartProcessing();
            UpdateStats();
        }

        [RelayCommand]
        private void ClearAll()
        {
            _transferQueueService.ClearAll();
            UpdateStats();
        }

        private void UpdateStats()
        {
            TotalCount = Tasks.Count;
            CompletedCount = Tasks.Count(t => t.Status == Models.Enums.TransferStatus.Completed);
            PendingCount = Tasks.Count(t => t.Status == Models.Enums.TransferStatus.Pending);
            FailedCount = Tasks.Count(t => t.Status == Models.Enums.TransferStatus.Failed);

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