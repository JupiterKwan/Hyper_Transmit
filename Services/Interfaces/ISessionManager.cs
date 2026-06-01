using Hyper_Transmit.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hyper_Transmit.Services.Interfaces
{
    /// <summary>
    /// Manages saved connection configurations (bookmarks/sites).
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>
        /// Gets all saved connections.
        /// </summary>
        Task<IReadOnlyList<ConnectionConfig>> GetAllAsync();

        /// <summary>
        /// Gets a connection by its ID.
        /// </summary>
        Task<ConnectionConfig?> GetByIdAsync(string id);

        /// <summary>
        /// Saves a connection (creates or updates).
        /// </summary>
        Task SaveAsync(ConnectionConfig config);

        /// <summary>
        /// Deletes a connection by its ID.
        /// </summary>
        Task DeleteAsync(string id);

        /// <summary>
        /// Gets all favorite connections.
        /// </summary>
        Task<IReadOnlyList<ConnectionConfig>> GetFavoritesAsync();

        /// <summary>
        /// Gets the most recently used connections.
        /// </summary>
        Task<IReadOnlyList<ConnectionConfig>> GetRecentAsync(int count = 5);

        /// <summary>
        /// Updates the LastConnected timestamp for a connection.
        /// </summary>
        Task UpdateLastConnectedAsync(string id);
    }
}