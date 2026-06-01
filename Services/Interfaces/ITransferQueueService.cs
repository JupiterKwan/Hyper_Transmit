using Hyper_Transmit.Models;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Hyper_Transmit.Services.Interfaces
{
    /// <summary>
    /// Event args for transfer task completion.
    /// </summary>
    public class TransferTaskCompletedEventArgs : EventArgs
    {
        public TransferTask Task { get; }
        public bool Success { get; }

        public TransferTaskCompletedEventArgs(TransferTask task, bool success)
        {
            Task = task;
            Success = success;
        }
    }

    /// <summary>
    /// Event args for transfer progress updates.
    /// </summary>
    public class TransferProgressEventArgs : EventArgs
    {
        public string TaskId { get; }
        public TransferProgress Progress { get; }

        public TransferProgressEventArgs(string taskId, TransferProgress progress)
        {
            TaskId = taskId;
            Progress = progress;
        }
    }

    /// <summary>
    /// Manages the transfer queue with support for concurrent transfers,
    /// pause/resume, cancel, and retry.
    /// </summary>
    public interface ITransferQueueService
    {
        /// <summary>
        /// All transfer tasks (including completed/cancelled/failed).
        /// </summary>
        ObservableCollection<TransferTask> Tasks { get; }

        /// <summary>
        /// Maximum number of concurrent transfers.
        /// </summary>
        int MaxConcurrentTransfers { get; set; }

        /// <summary>
        /// Number of currently active (transferring) tasks.
        /// </summary>
        int ActiveTransferCount { get; }

        /// <summary>
        /// Overall transfer speed (bytes/sec) across all active transfers.
        /// </summary>
        double OverallSpeed { get; }

        event EventHandler<TransferTaskCompletedEventArgs>? TaskCompleted;
        event EventHandler<TransferProgressEventArgs>? ProgressChanged;
        event EventHandler? QueueChanged;
        event EventHandler<TransferTask>? ResumeRequested;

        /// <summary>
        /// Enqueues a file upload task.
        /// </summary>
        string EnqueueUpload(string connectionId, string connectionName,
            string localPath, string remotePath, long fileSize);

        /// <summary>
        /// Enqueues a file download task.
        /// </summary>
        string EnqueueDownload(string connectionId, string connectionName,
            string remotePath, string localPath, long fileSize);

        /// <summary>
        /// Starts processing the queue. Call after enqueuing tasks.
        /// </summary>
        void StartProcessing();

        /// <summary>
        /// Pauses a specific transfer task.
        /// </summary>
        Task PauseTaskAsync(string taskId);

        /// <summary>
        /// Resumes a specific transfer task.
        /// </summary>
        Task ResumeTaskAsync(string taskId);

        /// <summary>
        /// Cancels a specific transfer task.
        /// </summary>
        Task CancelTaskAsync(string taskId);

        /// <summary>
        /// Pauses all active transfers.
        /// </summary>
        Task PauseAllAsync();

        /// <summary>
        /// Resumes all paused transfers.
        /// </summary>
        Task ResumeAllAsync();

        /// <summary>
        /// Cancels all transfers.
        /// </summary>
        Task CancelAllAsync();

        /// <summary>
        /// Retries a failed transfer task.
        /// </summary>
        Task RetryTaskAsync(string taskId);

        /// <summary>
        /// Removes all completed, failed, and cancelled tasks from the list.
        /// </summary>
        void ClearCompleted();

        /// <summary>
        /// Removes all tasks from the list.
        /// </summary>
        void ClearAll();
    }
}