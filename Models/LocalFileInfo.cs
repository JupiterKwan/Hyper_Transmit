using System;
using System.IO;

namespace Hyper_Transmit.Models
{
    /// <summary>
    /// Represents a file or directory on the local file system.
    /// </summary>
    public class LocalFileInfo
    {
        public string Name { get; set; } = "";

        public string FullPath { get; set; } = "";

        public long Size { get; set; }

        public bool IsDirectory { get; set; }

        public bool IsHidden { get; set; }

        public DateTime LastModified { get; set; }

        public string Extension { get; set; } = "";

        /// <summary>
        /// Returns a human-readable size string.
        /// </summary>
        public string SizeDisplay => IsDirectory ? "<DIR>" : FormatFileSize(Size);

        /// <summary>
        /// Creates a LocalFileInfo from a FileSystemInfo.
        /// </summary>
        public static LocalFileInfo FromFileSystemInfo(FileSystemInfo info)
        {
            var isDir = info is DirectoryInfo;
            var lfi = new LocalFileInfo
            {
                Name = info.Name,
                FullPath = info.FullName,
                IsDirectory = isDir,
                IsHidden = info.Attributes.HasFlag(FileAttributes.Hidden),
                LastModified = info.LastWriteTime,
                Extension = isDir ? "" : info.Extension
            };

            if (!isDir && info is FileInfo fi)
            {
                lfi.Size = fi.Length;
            }

            return lfi;
        }

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