using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyper_Transmit.Models;
using Hyper_Transmit.Models.Enums;
using Hyper_Transmit.Services;
using Hyper_Transmit.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Hyper_Transmit.ViewModels
{
    /// <summary>
    /// ViewModel for the Connection Manager page.
    /// Manages saved connections (CRUD operations).
    /// </summary>
    public partial class ConnectionManagerViewModel : BaseViewModel
    {
        private readonly ISessionManager _sessionManager;
        private readonly CredentialService _credentialService;

        [ObservableProperty]
        private ObservableCollection<ConnectionConfig> _connections = new();

        [ObservableProperty]
        private ConnectionConfig? _selectedConnection;

        [ObservableProperty]
        private string _searchText = "";

        [ObservableProperty]
        private bool _isEditing;

        // Edit form fields
        [ObservableProperty]
        private string _editName = "";

        [ObservableProperty]
        private string _editHost = "";

        [ObservableProperty]
        private int _editPort = 22;

        [ObservableProperty]
        private string _editUsername = "";

        [ObservableProperty]
        private string _editPassword = "";

        [ObservableProperty]
        private string _editPrivateKeyPath = "";

        [ObservableProperty]
        private string _editLocalPath = "";

        [ObservableProperty]
        private string _editRemotePath = "/";

        [ObservableProperty]
        private ProtocolType _editProtocol = ProtocolType.SFTP;

        [ObservableProperty]
        private AuthenticationType _editAuthType = AuthenticationType.Password;

        [ObservableProperty]
        private bool _editIsFavorite;

        [ObservableProperty]
        private string _editNotes = "";

        public ConnectionManagerViewModel(ISessionManager sessionManager, CredentialService credentialService)
        {
            _sessionManager = sessionManager;
            _credentialService = credentialService;
        }

        /// <summary>
        /// Loads all saved connections.
        /// </summary>
        [RelayCommand]
        private async Task LoadConnectionsAsync()
        {
            IsLoading = true;
            try
            {
                var connections = await _sessionManager.GetAllAsync();
                Connections = new ObservableCollection<ConnectionConfig>(connections);
            }
            catch (Exception ex)
            {
                SetError($"Failed to load connections: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Selects a connection and populates the edit form.
        /// </summary>
        [RelayCommand]
        private void SelectConnection(ConnectionConfig? connection)
        {
            SelectedConnection = connection;
            if (connection != null)
            {
                PopulateEditForm(connection);
                IsEditing = true;
            }
            else
            {
                IsEditing = false;
            }
        }

        /// <summary>
        /// Creates a new empty connection for editing.
        /// </summary>
        [RelayCommand]
        private void NewConnection()
        {
            var newConn = new ConnectionConfig
            {
                Name = "New Connection",
                Protocol = ProtocolType.SFTP,
                Port = 22,
                AuthType = AuthenticationType.Password,
                RemoteDefaultPath = "/"
            };
            SelectedConnection = newConn;
            PopulateEditForm(newConn);
            IsEditing = true;
        }

        /// <summary>
        /// Saves the current edit form to the connection store.
        /// </summary>
        [RelayCommand]
        private async Task SaveConnectionAsync()
        {
            if (SelectedConnection == null) return;

            IsLoading = true;
            try
            {
                SelectedConnection.Name = EditName;
                SelectedConnection.Host = EditHost;
                SelectedConnection.Port = EditPort;
                SelectedConnection.Username = EditUsername;
                SelectedConnection.EncryptedPassword = _credentialService.Encrypt(EditPassword);
                SelectedConnection.PrivateKeyPath = EditPrivateKeyPath;
                SelectedConnection.LocalDefaultPath = EditLocalPath;
                SelectedConnection.RemoteDefaultPath = EditRemotePath;
                SelectedConnection.Protocol = EditProtocol;
                SelectedConnection.AuthType = EditAuthType;
                SelectedConnection.IsFavorite = EditIsFavorite;
                SelectedConnection.Notes = EditNotes;

                await _sessionManager.SaveAsync(SelectedConnection);
                await LoadConnectionsAsync();
                StatusMessage = $"Connection '{EditName}' saved.";
            }
            catch (Exception ex)
            {
                SetError($"Failed to save connection: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Deletes the selected connection.
        /// </summary>
        [RelayCommand]
        private async Task DeleteConnectionAsync()
        {
            if (SelectedConnection == null) return;

            IsLoading = true;
            try
            {
                await _sessionManager.DeleteAsync(SelectedConnection.Id);
                IsEditing = false;
                SelectedConnection = null;
                await LoadConnectionsAsync();
                StatusMessage = "Connection deleted.";
            }
            catch (Exception ex)
            {
                SetError($"Failed to delete connection: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Tests the connection by attempting to connect.
        /// </summary>
        [RelayCommand]
        private async Task TestConnectionAsync(ISshService sshService)
        {
            if (SelectedConnection == null) return;

            IsLoading = true;
            StatusMessage = "Testing connection...";
            try
            {
                var config = BuildConfigFromForm();
                await sshService.ConnectAsync(config);
                StatusMessage = $"✓ Connected to {config.Host} successfully!";
                await sshService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                SetError($"Connection failed: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Toggles favorite status for a connection.
        /// </summary>
        [RelayCommand]
        private async Task ToggleFavoriteAsync(ConnectionConfig connection)
        {
            connection.IsFavorite = !connection.IsFavorite;
            await _sessionManager.SaveAsync(connection);
            await LoadConnectionsAsync();
        }

        partial void OnSearchTextChanged(string value)
        {
            // TODO: Filter connections list based on search text
        }

        private void PopulateEditForm(ConnectionConfig config)
        {
            EditName = config.Name;
            EditHost = config.Host;
            EditPort = config.Port;
            EditUsername = config.Username;
            EditPassword = _credentialService.Decrypt(config.EncryptedPassword);
            EditPrivateKeyPath = config.PrivateKeyPath;
            EditLocalPath = config.LocalDefaultPath;
            EditRemotePath = config.RemoteDefaultPath;
            EditProtocol = config.Protocol;
            EditAuthType = config.AuthType;
            EditIsFavorite = config.IsFavorite;
            EditNotes = config.Notes ?? "";
        }

        private ConnectionConfig BuildConfigFromForm()
        {
            return new ConnectionConfig
            {
                Id = SelectedConnection?.Id ?? Guid.NewGuid().ToString(),
                Name = EditName,
                Host = EditHost,
                Port = EditPort,
                Username = EditUsername,
                EncryptedPassword = EditPassword,
                PrivateKeyPath = EditPrivateKeyPath,
                LocalDefaultPath = EditLocalPath,
                RemoteDefaultPath = EditRemotePath,
                Protocol = EditProtocol,
                AuthType = EditAuthType,
                IsFavorite = EditIsFavorite,
                Notes = EditNotes
            };
        }
    }
}