using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyper_Transmit.Models;
using Hyper_Transmit.Models.Enums;
using Hyper_Transmit.Services.Interfaces;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;

namespace Hyper_Transmit.ViewModels
{
    /// <summary>
    /// ViewModel for the Settings page.
    /// Note: SettingsPage.xaml.cs currently manages settings directly via ISettingsService.
    /// This ViewModel is kept for potential future use.
    /// </summary>
    public partial class SettingsViewModel : BaseViewModel
    {
        private readonly ISettingsService _settingsService;

        // General
        [ObservableProperty]
        private int _maxConcurrentTransfers = 3;

        [ObservableProperty]
        private bool _minimizeToTray;

        // Transfer
        [ObservableProperty]
        private bool _retryOnFailure = true;

        [ObservableProperty]
        private int _maxRetries = 3;

        [ObservableProperty]
        private OverwriteAction _defaultOverwriteAction = OverwriteAction.Ask;

        [ObservableProperty]
        private bool _soundOnComplete = true;

        [ObservableProperty]
        private bool _logTransferHistory = true;

        // Appearance
        [ObservableProperty]
        private ElementTheme _theme = ElementTheme.Default;

        [ObservableProperty]
        private bool _showHiddenFiles;

        [ObservableProperty]
        private string _dateFormat = "yyyy-MM-dd HH:mm";

        // Logging
        [ObservableProperty]
        private bool _enableLogging = true;

        [ObservableProperty]
        private int _logLevel = 1;

        public SettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        [RelayCommand]
        private async Task LoadSettingsAsync()
        {
            await _settingsService.LoadAsync();
            var s = _settingsService.Settings;

            MaxConcurrentTransfers = s.MaxConcurrentTransfers;
            MinimizeToTray = s.MinimizeToTray;
            RetryOnFailure = s.RetryOnFailure;
            MaxRetries = s.MaxRetries;
            DefaultOverwriteAction = s.DefaultOverwriteAction;
            SoundOnComplete = s.SoundOnComplete;
            LogTransferHistory = s.LogTransferHistory;
            Theme = s.Theme;
            ShowHiddenFiles = s.ShowHiddenFiles;
            DateFormat = s.DateFormat;
            EnableLogging = s.EnableLogging;
            LogLevel = s.LogLevel;
        }

        [RelayCommand]
        private async Task SaveSettingsAsync()
        {
            var s = _settingsService.Settings;

            s.MaxConcurrentTransfers = MaxConcurrentTransfers;
            s.MinimizeToTray = MinimizeToTray;
            s.RetryOnFailure = RetryOnFailure;
            s.MaxRetries = MaxRetries;
            s.DefaultOverwriteAction = DefaultOverwriteAction;
            s.SoundOnComplete = SoundOnComplete;
            s.LogTransferHistory = LogTransferHistory;
            s.Theme = Theme;
            s.ShowHiddenFiles = ShowHiddenFiles;
            s.DateFormat = DateFormat;
            s.EnableLogging = EnableLogging;
            s.LogLevel = LogLevel;

            await _settingsService.SaveAsync();
            StatusMessage = "设置已保存";
        }

        [RelayCommand]
        private async Task ResetDefaultsAsync()
        {
            _settingsService.Settings = new AppSettings();
            await LoadSettingsAsync();
            StatusMessage = "已恢复默认设置";
        }
    }
}