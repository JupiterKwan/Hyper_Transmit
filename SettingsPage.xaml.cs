using Hyper_Transmit.Models;
using Hyper_Transmit.Models.Enums;
using Hyper_Transmit.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;

namespace Hyper_Transmit
{
    public sealed partial class SettingsPage : Page
    {
        private readonly ISettingsService _settingsService;
        private readonly ITransferQueueService _transferQueueService;
        private bool _isLoaded;
        private string _currentPanel = "General";

        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HyperTransmit", "logs");

        public SettingsPage()
        {
            InitializeComponent();
            _settingsService = App.Services.GetRequiredService<ISettingsService>();
            _transferQueueService = App.Services.GetRequiredService<ITransferQueueService>();
            Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = _settingsService.Settings;

                // General
                ConcurrentTransfersCombo.SelectedIndex = s.MaxConcurrentTransfers switch
                {
                    1 => 0, 2 => 1, 3 => 2, 5 => 3, 10 => 4, _ => 2
                };
                MinimizeToTraySwitch.IsOn = s.MinimizeToTray;

                // Transfer
                RetryOnFailureSwitch.IsOn = s.RetryOnFailure;
                MaxRetriesBox.Value = s.MaxRetries;
                OverwriteActionCombo.SelectedIndex = (int)s.DefaultOverwriteAction;
                SoundOnCompleteSwitch.IsOn = s.SoundOnComplete;
                LogTransferHistorySwitch.IsOn = s.LogTransferHistory;

                // Appearance
                ThemeCombo.SelectedIndex = s.Theme switch
                {
                    ElementTheme.Light => 1,
                    ElementTheme.Dark => 2,
                    _ => 0
                };
                ShowHiddenFilesSwitch.IsOn = s.ShowHiddenFiles;
                DateFormatCombo.SelectedIndex = s.DateFormat switch
                {
                    "dd/MM/yyyy HH:mm" => 1,
                    "MM/dd/yyyy hh:mm tt" => 2,
                    "yyyy年MM月dd日 HH:mm" => 3,
                    _ => 0
                };

                // Logging
                EnableLoggingSwitch.IsOn = s.EnableLogging;
                LogLevelCombo.SelectedIndex = s.LogLevel switch
                {
                    0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, _ => 1
                };
                LogPathText.Text = LogDir;

                // Show the correct panel
                ShowPanel(_currentPanel);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsPage_Loaded error: {ex.Message}");
            }
            finally
            {
                _isLoaded = true;
            }
        }

        private void ShowPanel(string panelName)
        {
            _currentPanel = panelName;
            GeneralPanel.Visibility = panelName == "General" ? Visibility.Visible : Visibility.Collapsed;
            TransferPanel.Visibility = panelName == "Transfer" ? Visibility.Visible : Visibility.Collapsed;
            AppearancePanel.Visibility = panelName == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
            LoggingPanel.Visibility = panelName == "Logging" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (SettingsNav.SelectedItem is not ListViewItem item) return;
            var tag = item.Tag as string;
            if (!string.IsNullOrEmpty(tag))
                ShowPanel(tag);
        }

        #region Immediate Apply Helpers

        private void SaveAndApply()
        {
            var s = _settingsService.Settings;
            // Update all settings from UI
            s.MaxConcurrentTransfers = ConcurrentTransfersCombo.SelectedIndex switch
            {
                0 => 1, 1 => 2, 2 => 3, 3 => 5, 4 => 10, _ => 3
            };
            s.MinimizeToTray = MinimizeToTraySwitch.IsOn;
            s.RetryOnFailure = RetryOnFailureSwitch.IsOn;
            s.MaxRetries = (int)MaxRetriesBox.Value;
            s.DefaultOverwriteAction = (OverwriteAction)OverwriteActionCombo.SelectedIndex;
            s.SoundOnComplete = SoundOnCompleteSwitch.IsOn;
            s.LogTransferHistory = LogTransferHistorySwitch.IsOn;
            s.Theme = ThemeCombo.SelectedIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
            s.ShowHiddenFiles = ShowHiddenFilesSwitch.IsOn;
            s.DateFormat = DateFormatCombo.SelectedIndex switch
            {
                1 => "dd/MM/yyyy HH:mm",
                2 => "MM/dd/yyyy hh:mm tt",
                3 => "yyyy年MM月dd日 HH:mm",
                _ => "yyyy-MM-dd HH:mm"
            };
            s.EnableLogging = EnableLoggingSwitch.IsOn;
            s.LogLevel = LogLevelCombo.SelectedIndex;

            // Persist
            _ = _settingsService.SaveAsync();

            // Apply to services
            _transferQueueService.MaxConcurrentTransfers = s.MaxConcurrentTransfers;

            // Apply theme
            if (App.MainWindow is MainWindow mainWindow)
            {
                if (mainWindow.Content is FrameworkElement root)
                    root.RequestedTheme = s.Theme;
            }

            // Apply to HomePage (hidden files, date format refresh)
            if (App.MainWindow is MainWindow mw)
            {
                mw.NotifyHomePageSettingsChanged(s);
            }
        }

        #endregion

        #region Event Handlers (all trigger immediate save+apply)

        private void ConcurrentTransfersCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SaveAndApply();
        }

        private void Setting_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SaveAndApply();
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            // Apply theme immediately for preview
            var theme = ThemeCombo.SelectedIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
            if (App.MainWindow is MainWindow mainWindow)
            {
                if (mainWindow.Content is FrameworkElement root)
                    root.RequestedTheme = theme;
            }
            SaveAndApply();
        }

        private void DateFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SaveAndApply();
        }

        private void OverwriteActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SaveAndApply();
        }

        private void MaxRetriesBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (!_isLoaded) return;
            SaveAndApply();
        }

        private void LogLevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SaveAndApply();
        }

        #endregion

        #region Reset

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _settingsService.Settings = new AppSettings();
            var s = _settingsService.Settings;

            // Reload UI
            _isLoaded = false;
            ConcurrentTransfersCombo.SelectedIndex = 2;
            MinimizeToTraySwitch.IsOn = false;
            RetryOnFailureSwitch.IsOn = true;
            MaxRetriesBox.Value = 3;
            OverwriteActionCombo.SelectedIndex = 0;
            SoundOnCompleteSwitch.IsOn = true;
            LogTransferHistorySwitch.IsOn = true;
            ThemeCombo.SelectedIndex = 0;
            ShowHiddenFilesSwitch.IsOn = false;
            DateFormatCombo.SelectedIndex = 0;
            EnableLoggingSwitch.IsOn = true;
            LogLevelCombo.SelectedIndex = 1;
            _isLoaded = true;

            // Save and apply
            _ = _settingsService.SaveAsync();
            _transferQueueService.MaxConcurrentTransfers = s.MaxConcurrentTransfers;

            if (App.MainWindow is MainWindow mainWindow)
            {
                if (mainWindow.Content is FrameworkElement root)
                    root.RequestedTheme = s.Theme;
                mainWindow.NotifyHomePageSettingsChanged(s);
            }
        }

        #endregion

        #region Logging

        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(LogDir);
            Process.Start(new ProcessStartInfo { FileName = LogDir, UseShellExecute = true });
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(LogDir))
            {
                foreach (var file in Directory.GetFiles(LogDir, "*.log"))
                    try { File.Delete(file); } catch { }
            }
            // Also clear transfer history
            var historyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HyperTransmit", "history");
            if (Directory.Exists(historyDir))
            {
                foreach (var file in Directory.GetFiles(historyDir, "*.log"))
                    try { File.Delete(file); } catch { }
            }
        }

        #endregion
    }
}