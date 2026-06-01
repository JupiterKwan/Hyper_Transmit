using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyper_Transmit.Models;
using Hyper_Transmit.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Hyper_Transmit.ViewModels
{
    /// <summary>
    /// ViewModel for a single file browser panel (local or remote).
    /// </summary>
    public partial class FileBrowserViewModel : BaseViewModel
    {
        private readonly ILocalFileService? _localFileService;
        private readonly ISshService? _sshService;

        [ObservableProperty]
        private ObservableCollection<LocalFileInfo> _localFiles = new();

        [ObservableProperty]
        private ObservableCollection<RemoteFileInfo> _remoteFiles = new();

        [ObservableProperty]
        private string _currentPath = "";

        [ObservableProperty]
        private bool _isRemote;

        [ObservableProperty]
        private LocalFileInfo? _selectedLocalItem;

        [ObservableProperty]
        private RemoteFileInfo? _selectedRemoteItem;

        [ObservableProperty]
        private int _selectedItemCount;

        [ObservableProperty]
        private int _totalItemCount;

        [ObservableProperty]
        private string _selectedItemInfo = "";

        /// <summary>
        /// Whether to show hidden files (files starting with '.'). 
        /// Set from settings.
        /// </summary>
        public bool ShowHiddenFiles { get; set; }

        /// <summary>
        /// Creates a local file browser panel.
        /// </summary>
        public FileBrowserViewModel(ILocalFileService localFileService)
        {
            _localFileService = localFileService;
            IsRemote = false;
        }

        /// <summary>
        /// Creates a remote file browser panel.
        /// </summary>
        public FileBrowserViewModel(ISshService sshService)
        {
            _sshService = sshService;
            IsRemote = true;
        }

        /// <summary>
        /// Navigates to the specified path and loads its contents.
        /// </summary>
        [RelayCommand]
        public async Task NavigateToAsync(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;

            IsLoading = true;
            ClearError();
            try
            {
                if (IsRemote)
                {
                    await LoadRemoteDirectoryAsync(path);
                }
                else
                {
                    await LoadLocalDirectoryAsync(path);
                }
                CurrentPath = path;
            }
            catch (Exception ex)
            {
                SetError($"Failed to load directory: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Navigates to the parent directory.
        /// </summary>
        [RelayCommand]
        public async Task NavigateUpAsync()
        {
            if (string.IsNullOrEmpty(CurrentPath)) return;

            string? parentPath;
            if (IsRemote)
            {
                // For remote paths, go up one level
                var lastSlash = CurrentPath.LastIndexOf('/');
                parentPath = lastSlash > 0 ? CurrentPath[..lastSlash] : "/";
            }
            else
            {
                parentPath = _localFileService?.GetParentPath(CurrentPath);
            }

            if (parentPath != null)
            {
                await NavigateToAsync(parentPath);
            }
        }

        /// <summary>
        /// Refreshes the current directory.
        /// </summary>
        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (!string.IsNullOrEmpty(CurrentPath))
            {
                await NavigateToAsync(CurrentPath);
            }
        }

        /// <summary>
        /// Handles double-click on a local item (navigate into directory).
        /// </summary>
        [RelayCommand]
        private async Task OpenLocalItemAsync(LocalFileInfo? item)
        {
            if (item == null) return;

            if (item.Name == "..")
            {
                await NavigateUpAsync();
            }
            else if (item.IsDirectory)
            {
                await NavigateToAsync(item.FullPath);
            }
        }

        /// <summary>
        /// Handles double-click on a remote item (navigate into directory).
        /// </summary>
        [RelayCommand]
        private async Task OpenRemoteItemAsync(RemoteFileInfo? item)
        {
            if (item == null) return;

            if (item.IsDirectory)
            {
                await NavigateToAsync(item.FullPath);
            }
        }

        private async Task LoadLocalDirectoryAsync(string path)
        {
            if (_localFileService == null) return;

            var items = await _localFileService.ListDirectoryAsync(path);
            if (!ShowHiddenFiles)
            {
                items = items.Where(f => f.Name == ".." || !f.Name.StartsWith('.')).ToList();
            }
            LocalFiles = new ObservableCollection<LocalFileInfo>(items);
            RemoteFiles.Clear();
            TotalItemCount = items.Count;
            UpdateSelectionInfo();
        }

        private async Task LoadRemoteDirectoryAsync(string path)
        {
            if (_sshService == null || !_sshService.IsConnected) return;

            var items = await _sshService.ListDirectoryAsync(path);
            if (!ShowHiddenFiles)
            {
                items = items.Where(f => !f.Name.StartsWith('.')).ToList();
            }
            RemoteFiles = new ObservableCollection<RemoteFileInfo>(items);
            LocalFiles.Clear();
            TotalItemCount = items.Count;
            UpdateSelectionInfo();
        }

        private void UpdateSelectionInfo()
        {
            if (IsRemote)
            {
                SelectedItemInfo = SelectedRemoteItem != null
                    ? $"{SelectedRemoteItem.Name} ({SelectedRemoteItem.SizeDisplay})"
                    : "";
            }
            else
            {
                SelectedItemInfo = SelectedLocalItem != null
                    ? $"{SelectedLocalItem.Name} ({SelectedLocalItem.SizeDisplay})"
                    : "";
            }
        }

        partial void OnSelectedLocalItemChanged(LocalFileInfo? value)
        {
            UpdateSelectionInfo();
        }

        partial void OnSelectedRemoteItemChanged(RemoteFileInfo? value)
        {
            UpdateSelectionInfo();
        }
    }
}