using Hyper_Transmit.Models;
using Hyper_Transmit.Models.Enums;
using Hyper_Transmit.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace Hyper_Transmit.Services
{
    /// <summary>
    /// Manages the transfer queue with concurrent transfer support.
    /// </summary>
    public class TransferQueueService : ITransferQueueService
    {
        private readonly ConcurrentQueue<string> _pendingQueue = new();
        private readonly SemaphoreSlim _concurrencySemaphore;
        private readonly object _lock = new();
        private bool _processing;
        private readonly DispatcherQueue? _dispatcherQueue;

        public ObservableCollection<TransferTask> Tasks { get; } = new();

        public int MaxConcurrentTransfers
        {
            get => _concurrencySemaphore.CurrentCount;
            set { /* TODO: allow dynamic resize */ }
        }

        public int ActiveTransferCount => Tasks.Count(t => t.Status == TransferStatus.Transferring);

        public double OverallSpeed => Tasks
            .Where(t => t.Status == TransferStatus.Transferring)
            .Sum(t => t.Speed);

        public event EventHandler<TransferTaskCompletedEventArgs>? TaskCompleted;
        public event EventHandler<TransferProgressEventArgs>? ProgressChanged;
        public event EventHandler? QueueChanged;
        public event EventHandler<TransferTask>? ResumeRequested;

        public TransferQueueService()
        {
            _concurrencySemaphore = new SemaphoreSlim(3, 3);
            try { _dispatcherQueue = DispatcherQueue.GetForCurrentThread(); } catch { }
        }

        public string EnqueueUpload(string connectionId, string connectionName,
            string localPath, string remotePath, long fileSize)
        {
            var task = new TransferTask
            {
                FileName = System.IO.Path.GetFileName(localPath),
                SourcePath = localPath,
                DestinationPath = remotePath,
                TotalSize = fileSize,
                Direction = TransferDirection.Upload,
                Status = TransferStatus.Pending,
                ConnectionId = connectionId,
                ConnectionName = connectionName
            };

            Tasks.Add(task);
            _pendingQueue.Enqueue(task.Id);
            QueueChanged?.Invoke(this, EventArgs.Empty);

            return task.Id;
        }

        public string EnqueueDownload(string connectionId, string connectionName,
            string remotePath, string localPath, long fileSize)
        {
            var task = new TransferTask
            {
                FileName = System.IO.Path.GetFileName(remotePath),
                SourcePath = remotePath,
                DestinationPath = localPath,
                TotalSize = fileSize,
                Direction = TransferDirection.Download,
                Status = TransferStatus.Pending,
                ConnectionId = connectionId,
                ConnectionName = connectionName
            };

            Tasks.Add(task);
            _pendingQueue.Enqueue(task.Id);
            QueueChanged?.Invoke(this, EventArgs.Empty);

            return task.Id;
        }

        public void StartProcessing()
        {
            if (_processing) return;
            _processing = true;
            _ = ProcessQueueAsync();
        }

        private async Task ProcessQueueAsync()
        {
            while (_processing)
            {
                // Check for pending tasks
                while (_pendingQueue.TryDequeue(out var taskId))
                {
                    var task = Tasks.FirstOrDefault(t => t.Id == taskId);
                    if (task == null || task.Status != TransferStatus.Pending)
                        continue;

                    await _concurrencySemaphore.WaitAsync();
                    _ = ProcessTaskAsync(task);
                }

                // Wait a bit before checking again
                await Task.Delay(100);

                // Stop processing if no more pending tasks
                if (_pendingQueue.IsEmpty && ActiveTransferCount == 0)
                {
                    _processing = false;
                    break;
                }
            }
        }

        private async Task ProcessTaskAsync(TransferTask task)
        {
            try
            {
                task.Status = TransferStatus.Transferring;
                task.StartTime = DateTime.Now;
                task.CancellationTokenSource = new CancellationTokenSource();

                var progress = new Progress<TransferProgress>(p =>
                {
                    task.TransferredSize = p.BytesTransferred;
                    task.Speed = p.SpeedBytesPerSecond;
                    ProgressChanged?.Invoke(this, new TransferProgressEventArgs(task.Id, p));
                });

                // Note: Actual SSH service calls will be made by the caller
                // This service manages the queue state
                // The actual transfer execution is handled by the page/viewmodel
                // that has access to the ISshService

                // For now, simulate completion after task is picked up
                // Real implementation will call ISshService methods here
                await Task.Delay(100, task.CancellationTokenSource.Token);

                // Task will be marked as completed by the actual transfer executor
            }
            catch (OperationCanceledException)
            {
                if (task.Status != TransferStatus.Paused)
                {
                    task.Status = TransferStatus.Cancelled;
                    task.EndTime = DateTime.Now;
                    TaskCompleted?.Invoke(this, new TransferTaskCompletedEventArgs(task, false));
                }
            }
            catch (Exception ex)
            {
                task.Status = TransferStatus.Failed;
                task.ErrorMessage = ex.Message;
                task.EndTime = DateTime.Now;
                TaskCompleted?.Invoke(this, new TransferTaskCompletedEventArgs(task, false));
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        }

        /// <summary>
        /// Marks a task as completed successfully. Called by the transfer executor.
        /// </summary>
        public void MarkTaskCompleted(string taskId)
        {
            var task = Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.Status = TransferStatus.Completed;
                task.EndTime = DateTime.Now;
                task.TransferredSize = task.TotalSize;
                TaskCompleted?.Invoke(this, new TransferTaskCompletedEventArgs(task, true));
            }
        }

        /// <summary>
        /// Updates task progress. Called by the transfer executor.
        /// </summary>
        public void UpdateTaskProgress(string taskId, long bytesTransferred, double speed)
        {
            var task = Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.TransferredSize = bytesTransferred;
                task.Speed = speed;
            }
        }

        /// <summary>
        /// Marks a task as failed. Called by the transfer executor.
        /// </summary>
        public void MarkTaskFailed(string taskId, string errorMessage)
        {
            var task = Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.Status = TransferStatus.Failed;
                task.ErrorMessage = errorMessage;
                task.EndTime = DateTime.Now;
                TaskCompleted?.Invoke(this, new TransferTaskCompletedEventArgs(task, false));
            }
        }

        public async Task PauseTaskAsync(string taskId)
        {
            var task = Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null && task.Status == TransferStatus.Transferring)
            {
                // Use PauseEvent to block the transfer stream without breaking the connection.
                // Do NOT cancel CTS - that would destroy the SFTP connection.
                task.Status = TransferStatus.Paused;
                task.Speed = 0;
                task.PauseEvent.Reset(); // Block the transfer on the background thread
            }
            await Task.CompletedTask;
        }

        public async Task ResumeTaskAsync(string taskId)
        {
            var task = Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null && task.Status == TransferStatus.Paused)
            {
                // Unblock the transfer stream - it continues from where it left off.
                task.Status = TransferStatus.Transferring;
                task.Speed = 0;
                task.PauseEvent.Set(); // Unblock the transfer
            }
            await Task.CompletedTask;
        }

        public async Task CancelTaskAsync(string taskId)
        {
            var task = Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.CancellationTokenSource?.Cancel();
                task.Status = TransferStatus.Cancelled;
                task.EndTime = DateTime.Now;
            }
            await Task.CompletedTask;
        }

        public async Task PauseAllAsync()
        {
            foreach (var task in Tasks.Where(t => t.Status == TransferStatus.Transferring).ToList())
            {
                await PauseTaskAsync(task.Id);
            }
        }

        public async Task ResumeAllAsync()
        {
            foreach (var task in Tasks.Where(t => t.Status == TransferStatus.Paused).ToList())
            {
                await ResumeTaskAsync(task.Id);
            }
        }

        public async Task CancelAllAsync()
        {
            foreach (var task in Tasks.Where(t =>
                t.Status == TransferStatus.Transferring ||
                t.Status == TransferStatus.Pending).ToList())
            {
                await CancelTaskAsync(task.Id);
            }
        }

        public async Task RetryTaskAsync(string taskId)
        {
            var task = Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null && task.Status == TransferStatus.Failed)
            {
                task.Status = TransferStatus.Pending;
                task.ErrorMessage = "";
                task.TransferredSize = 0;
                task.Speed = 0;
                task.CancellationTokenSource = new CancellationTokenSource();
                _pendingQueue.Enqueue(task.Id);
                StartProcessing();
            }
            await Task.CompletedTask;
        }

        public void ClearCompleted()
        {
            var completed = Tasks.Where(t =>
                t.Status == TransferStatus.Completed ||
                t.Status == TransferStatus.Cancelled ||
                t.Status == TransferStatus.Failed ||
                t.Status == TransferStatus.Paused).ToList();

            foreach (var task in completed)
            {
                Tasks.Remove(task);
            }
        }

        public void ClearAll()
        {
            CancelAllAsync().Wait();
            Tasks.Clear();
        }
    }
}