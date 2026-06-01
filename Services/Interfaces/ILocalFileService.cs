using Hyper_Transmit.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hyper_Transmit.Services.Interfaces
{
    /// <summary>
    /// Service for interacting with the local file system.
    /// </summary>
    public interface ILocalFileService
    {
        /// <summary>
        /// Lists files and directories in the specified local path.
        /// </summary>
        Task<IReadOnlyList<LocalFileInfo>> ListDirectoryAsync(string path, bool showHidden = false);

        /// <summary>
        /// Gets all available logical drives on the system.
        /// </summary>
        IReadOnlyList<DriveInfo> GetDrives();

        /// <summary>
        /// Gets the parent directory path, or null if at root.
        /// </summary>
        string? GetParentPath(string path);

        /// <summary>
        /// Creates a directory at the specified path.
        /// </summary>
        Task CreateDirectoryAsync(string path);

        /// <summary>
        /// Deletes a file at the specified path.
        /// </summary>
        Task DeleteFileAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Deletes a directory at the specified path.
        /// </summary>
        Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default);

        /// <summary>
        /// Renames a file or directory.
        /// </summary>
        Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default);

        /// <summary>
        /// Gets file size in bytes.
        /// </summary>
        long GetFileSize(string path);

        /// <summary>
        /// Checks if a path exists (file or directory).
        /// </summary>
        bool Exists(string path);

        /// <summary>
        /// Checks if the path is a directory.
        /// </summary>
        bool IsDirectory(string path);

        /// <summary>
        /// Gets the user's home directory.
        /// </summary>
        string GetHomeDirectory();

        /// <summary>
        /// Gets the user's desktop directory.
        /// </summary>
        string GetDesktopDirectory();
    }
}