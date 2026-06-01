using Hyper_Transmit.Models;
using Hyper_Transmit.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hyper_Transmit.Services
{
    /// <summary>
    /// Implementation of ILocalFileService for Windows file system operations.
    /// </summary>
    public class LocalFileService : ILocalFileService
    {
        public Task<IReadOnlyList<LocalFileInfo>> ListDirectoryAsync(string path, bool showHidden = false)
        {
            return Task.Run(() =>
            {
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists)
                    throw new DirectoryNotFoundException($"Directory not found: {path}");

                var items = new List<LocalFileInfo>();

                // Add parent directory entry (..) unless at root
                if (dirInfo.Parent != null)
                {
                    items.Add(new LocalFileInfo
                    {
                        Name = "..",
                        FullPath = dirInfo.Parent.FullName,
                        IsDirectory = true,
                        LastModified = dirInfo.Parent.LastWriteTime
                    });
                }

                // Add directories first
                foreach (var dir in dirInfo.GetDirectories()
                    .Where(d => showHidden || !d.Attributes.HasFlag(FileAttributes.Hidden))
                    .OrderBy(d => d.Name))
                {
                    items.Add(LocalFileInfo.FromFileSystemInfo(dir));
                }

                // Then add files
                foreach (var file in dirInfo.GetFiles()
                    .Where(f => showHidden || !f.Attributes.HasFlag(FileAttributes.Hidden))
                    .OrderBy(f => f.Name))
                {
                    items.Add(LocalFileInfo.FromFileSystemInfo(file));
                }

                return (IReadOnlyList<LocalFileInfo>)items;
            });
        }

        public IReadOnlyList<DriveInfo> GetDrives()
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .ToList()
                .AsReadOnly();
        }

        public string? GetParentPath(string path)
        {
            var dirInfo = new DirectoryInfo(path);
            return dirInfo.Parent?.FullName;
        }

        public Task CreateDirectoryAsync(string path)
        {
            return Task.Run(() => Directory.CreateDirectory(path));
        }

        public Task DeleteFileAsync(string path, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(path))
                    File.Delete(path);
            }, ct);
        }

        public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive);
            }, ct);
        }

        public Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(oldPath))
                {
                    File.Move(oldPath, newPath);
                }
                else if (Directory.Exists(oldPath))
                {
                    Directory.Move(oldPath, newPath);
                }
                else
                {
                    throw new FileNotFoundException($"Path not found: {oldPath}");
                }
            }, ct);
        }

        public long GetFileSize(string path)
        {
            return new FileInfo(path).Length;
        }

        public bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        public bool IsDirectory(string path)
        {
            return Directory.Exists(path);
        }

        public string GetHomeDirectory()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public string GetDesktopDirectory()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }
    }
}