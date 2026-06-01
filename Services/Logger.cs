using Hyper_Transmit.Services.Interfaces;
using System;
using System.IO;

namespace Hyper_Transmit.Services
{
    /// <summary>
    /// Simple file logger that writes to a log file based on the EnableLogging and LogLevel settings.
    /// </summary>
    public class Logger
    {
        private readonly ISettingsService _settingsService;
        private readonly string _logDir;
        private readonly object _lock = new();

        public Logger(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HyperTransmit", "logs");
        }

        public void Log(int level, string message, string? source = null)
        {
            try
            {
                var settings = _settingsService.Settings;
                if (!settings.EnableLogging) return;
                if (level < settings.LogLevel) return;

                Directory.CreateDirectory(_logDir);
                var logFile = Path.Combine(_logDir, $"app-{DateTime.Now:yyyy-MM-dd}.log");
                var levelStr = level switch
                {
                    0 => "VRB",
                    1 => "DBG",
                    2 => "INF",
                    3 => "WRN",
                    4 => "ERR",
                    _ => "INF"
                };
                var sourceStr = string.IsNullOrEmpty(source) ? "" : $" [{source}]";
                var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{levelStr}]{sourceStr} {message}\n";

                lock (_lock)
                {
                    File.AppendAllText(logFile, entry);
                }
            }
            catch { /* ignore log errors */ }
        }

        public void Verbose(string message, string? source = null) => Log(0, message, source);
        public void Debug(string message, string? source = null) => Log(1, message, source);
        public void Info(string message, string? source = null) => Log(2, message, source);
        public void Warning(string message, string? source = null) => Log(3, message, source);
        public void Error(string message, string? source = null) => Log(4, message, source);
        public void Error(string message, Exception ex, string? source = null) => Log(4, $"{message}: {ex}", source);
    }
}