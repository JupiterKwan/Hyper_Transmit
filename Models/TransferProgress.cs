using System;

namespace Hyper_Transmit.Models
{
    /// <summary>
    /// Progress information reported during file transfers.
    /// </summary>
    public class TransferProgress
    {
        public long BytesTransferred { get; set; }

        public long TotalBytes { get; set; }

        public double SpeedBytesPerSecond { get; set; }

        public TimeSpan Elapsed { get; set; }

        public string CurrentFileName { get; set; } = "";

        public double Percentage => TotalBytes > 0
            ? Math.Min(100.0, (double)BytesTransferred / TotalBytes * 100)
            : 0;

        public TransferProgress(long bytesTransferred, long totalBytes, double speedBytesPerSecond, TimeSpan elapsed)
        {
            BytesTransferred = bytesTransferred;
            TotalBytes = totalBytes;
            SpeedBytesPerSecond = speedBytesPerSecond;
            Elapsed = elapsed;
        }

        public TransferProgress(long bytesTransferred, long totalBytes, double speedBytesPerSecond, TimeSpan elapsed, string currentFileName)
        {
            BytesTransferred = bytesTransferred;
            TotalBytes = totalBytes;
            SpeedBytesPerSecond = speedBytesPerSecond;
            Elapsed = elapsed;
            CurrentFileName = currentFileName;
        }
    }
}