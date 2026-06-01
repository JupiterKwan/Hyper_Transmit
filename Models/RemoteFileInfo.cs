using System;

namespace Hyper_Transmit.Models
{
    /// <summary>
    /// Represents a file or directory on the remote server.
    /// </summary>
    public class RemoteFileInfo
    {
        public string Name { get; set; } = "";

        public string FullPath { get; set; } = "";

        public long Size { get; set; }

        public bool IsDirectory { get; set; }

        public bool IsSymlink { get; set; }

        public DateTime LastModified { get; set; }

        public string Owner { get; set; } = "";

        public string Group { get; set; } = "";

        /// <summary>
        /// Unix-style permissions string, e.g. "rwxr-xr-x".
        /// </summary>
        public string Permissions { get; set; } = "";

        /// <summary>
        /// Numeric permissions (e.g. 755).
        /// </summary>
        public int PermissionsOctal { get; set; }

        /// <summary>
        /// Returns a human-readable size string.
        /// </summary>
        public string SizeDisplay => IsDirectory ? "<DIR>" : FormatFileSize(Size);

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {suffixes[order]}";
        }
    }
}