using Hyper_Transmit.Models;
using Hyper_Transmit.Services.Interfaces;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Hyper_Transmit.Services
{
    /// <summary>
    /// Manages application settings persistence as JSON file.
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly string _filePath;

        public AppSettings Settings { get; set; } = new();

        public SettingsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(appData, "HyperTransmit");
            Directory.CreateDirectory(appDir);
            _filePath = Path.Combine(appDir, "settings.json");
        }

        public async Task LoadAsync()
        {
            if (!File.Exists(_filePath))
            {
                Settings = new AppSettings();
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_filePath);
                Settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception)
            {
                Settings = new AppSettings();
            }
        }

        public async Task SaveAsync()
        {
            var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            await File.WriteAllTextAsync(_filePath, json);
        }
    }
}