using Hyper_Transmit.Models;
using Hyper_Transmit.Services.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Hyper_Transmit.Services
{
    /// <summary>
    /// Manages saved connection configurations stored as JSON file.
    /// </summary>
    public class SessionManager : ISessionManager
    {
        private readonly string _filePath;
        private List<ConnectionConfig> _connections = new();
        private readonly object _lock = new();

        public SessionManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(appData, "HyperTransmit");
            Directory.CreateDirectory(appDir);
            _filePath = Path.Combine(appDir, "connections.json");
        }

        public async Task<IReadOnlyList<ConnectionConfig>> GetAllAsync()
        {
            await EnsureLoadedAsync();
            lock (_lock)
            {
                return _connections.ToList().AsReadOnly();
            }
        }

        public async Task<ConnectionConfig?> GetByIdAsync(string id)
        {
            await EnsureLoadedAsync();
            lock (_lock)
            {
                return _connections.FirstOrDefault(c => c.Id == id);
            }
        }

        public async Task SaveAsync(ConnectionConfig config)
        {
            await EnsureLoadedAsync();
            lock (_lock)
            {
                var existing = _connections.FirstOrDefault(c => c.Id == config.Id);
                if (existing != null)
                {
                    var index = _connections.IndexOf(existing);
                    _connections[index] = config;
                }
                else
                {
                    _connections.Add(config);
                }
            }
            await SaveToFileAsync();
        }

        public async Task DeleteAsync(string id)
        {
            await EnsureLoadedAsync();
            lock (_lock)
            {
                _connections.RemoveAll(c => c.Id == id);
            }
            await SaveToFileAsync();
        }

        public async Task<IReadOnlyList<ConnectionConfig>> GetFavoritesAsync()
        {
            await EnsureLoadedAsync();
            lock (_lock)
            {
                return _connections.Where(c => c.IsFavorite).ToList().AsReadOnly();
            }
        }

        public async Task<IReadOnlyList<ConnectionConfig>> GetRecentAsync(int count = 5)
        {
            await EnsureLoadedAsync();
            lock (_lock)
            {
                return _connections
                    .Where(c => c.LastConnected.HasValue)
                    .OrderByDescending(c => c.LastConnected)
                    .Take(count)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public async Task UpdateLastConnectedAsync(string id)
        {
            await EnsureLoadedAsync();
            lock (_lock)
            {
                var existing = _connections.FirstOrDefault(c => c.Id == id);
                if (existing != null)
                {
                    existing.LastConnected = DateTime.Now;
                }
            }
            await SaveToFileAsync();
        }

        private bool _loaded;

        private async Task EnsureLoadedAsync()
        {
            if (_loaded) return;
            await LoadFromFileAsync();
            _loaded = true;
        }

        private async Task LoadFromFileAsync()
        {
            if (!File.Exists(_filePath))
                return;

            try
            {
                var json = await File.ReadAllTextAsync(_filePath);
                var connections = JsonConvert.DeserializeObject<List<ConnectionConfig>>(json);
                lock (_lock)
                {
                    _connections = connections ?? new List<ConnectionConfig>();
                }
            }
            catch (Exception)
            {
                // If the file is corrupt, start fresh
                lock (_lock)
                {
                    _connections = new List<ConnectionConfig>();
                }
            }
        }

        private async Task SaveToFileAsync()
        {
            List<ConnectionConfig> snapshot;
            lock (_lock)
            {
                snapshot = _connections.ToList();
            }
            var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            await File.WriteAllTextAsync(_filePath, json);
        }
    }
}