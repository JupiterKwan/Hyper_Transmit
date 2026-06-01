using Hyper_Transmit.Models.Enums;
using Microsoft.UI.Xaml;

namespace Hyper_Transmit.Models
{
    /// <summary>
    /// Application-wide settings. All changes are applied immediately.
    /// </summary>
    public class AppSettings
    {
        // === General ===
        public int MaxConcurrentTransfers { get; set; } = 3;
        public bool MinimizeToTray { get; set; }

        // === Transfer ===
        public bool RetryOnFailure { get; set; } = true;
        public int MaxRetries { get; set; } = 3;
        public OverwriteAction DefaultOverwriteAction { get; set; } = OverwriteAction.Ask;
        public bool SoundOnComplete { get; set; } = true;
        public bool LogTransferHistory { get; set; } = true;

        // === Appearance ===
        public ElementTheme Theme { get; set; } = ElementTheme.Default;
        public bool ShowHiddenFiles { get; set; }
        public string DateFormat { get; set; } = "yyyy-MM-dd HH:mm";

        // === State ===
        public string LastLocalPath { get; set; } = "";

        // === Logging ===
        public bool EnableLogging { get; set; } = true;
        public int LogLevel { get; set; } = 1; // 0=Verbose, 1=Debug, 2=Info, 3=Warning, 4=Error

        // === Window State ===
        public double WindowWidth { get; set; } = 1200;
        public double WindowHeight { get; set; } = 800;
        public bool IsMaximized { get; set; }
    }
}