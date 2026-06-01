using Hyper_Transmit.Models;
using System.Threading.Tasks;

namespace Hyper_Transmit.Services.Interfaces
{
    /// <summary>
    /// Manages application settings persistence.
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// Current application settings.
        /// </summary>
        AppSettings Settings { get; set; }

        /// <summary>
        /// Loads settings from persistent storage.
        /// </summary>
        Task LoadAsync();

        /// <summary>
        /// Saves current settings to persistent storage.
        /// </summary>
        Task SaveAsync();
    }
}