using Hyper_Transmit.Models;
using Hyper_Transmit.Models.Enums;
using Hyper_Transmit.Services;
using Hyper_Transmit.Services.Interfaces;
using Hyper_Transmit.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace Hyper_Transmit
{
    /// <summary>
    /// Connection Manager page - manage saved SSH connections.
    /// </summary>
    public sealed partial class ConnectionManagerPage : Page
    {
        private readonly ConnectionManagerViewModel _viewModel;
        private readonly ISshService _sshService;

        public ConnectionManagerPage()
        {
            InitializeComponent();

            _viewModel = App.Services.GetRequiredService<ConnectionManagerViewModel>();
            _sshService = App.Services.GetRequiredService<ISshService>();
            DataContext = _viewModel;

            Loaded += ConnectionManagerPage_Loaded;
        }

        private async void ConnectionManagerPage_Loaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.LoadConnectionsCommand.ExecuteAsync(null);
            ConnectionList.ItemsSource = _viewModel.Connections;
        }

        private void NewConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NewConnectionCommand.Execute(null);
            ShowEditForm();
            ClearEditForm();
            EditNameBox.Text = "New Connection";
            EditPortBox.Value = 22;
            EditProtocolCombo.SelectedIndex = 0;
            EditAuthTypeCombo.SelectedIndex = 0;
            EditRemotePathBox.Text = "/";
        }

        private async void DeleteConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedConnection == null) return;

            await _viewModel.DeleteConnectionCommand.ExecuteAsync(null);
            HideEditForm();
            ConnectionList.ItemsSource = _viewModel.Connections;
        }

        private void ConnectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = ConnectionList.SelectedItem as ConnectionConfig;
            if (selected != null)
            {
                _viewModel.SelectConnectionCommand.Execute(selected);
                PopulateEditForm(selected);
                ShowEditForm();
                DeleteConnectionButton.IsEnabled = true;
            }
            else
            {
                HideEditForm();
                DeleteConnectionButton.IsEnabled = false;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ReadEditForm();
            await _viewModel.SaveConnectionCommand.ExecuteAsync(null);
            ConnectionList.ItemsSource = _viewModel.Connections;
            StatusText.Text = _viewModel.StatusMessage;
        }

        private static ConnectionConfig? _pendingConnectConfig;

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedConnection == null) return;

            ReadEditForm();
            await _viewModel.SaveConnectionCommand.ExecuteAsync(null);

            var mainWindow = App.MainWindow;
            if (mainWindow == null) return;

            // Store config for HomePage to auto-connect
            _pendingConnectConfig = _viewModel.SelectedConnection;

            // Navigate to HomePage
            mainWindow.RootFrame.Navigate(typeof(HomePage));
        }

        /// <summary>
        /// Called by HomePage to consume pending config for auto-connect.
        /// Password is encrypted in EncryptedPassword - SshService will decrypt it.
        /// </summary>
        public static ConnectionConfig? ConsumePendingConfig()
        {
            var config = _pendingConnectConfig;
            _pendingConnectConfig = null;
            return config;
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "测试连接中...";
            ReadEditForm();
            await _viewModel.TestConnectionCommand.ExecuteAsync(_sshService);
            StatusText.Text = _viewModel.HasError ? _viewModel.ErrorMessage : _viewModel.StatusMessage;
        }

        private async void BrowseKeyButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".pem");
            picker.FileTypeFilter.Add(".ppk");
            picker.FileTypeFilter.Add(".key");

            // WinUI 3 requires setting the window handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(Window.Current);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                EditKeyPathBox.Text = file.Path;
            }
        }

        #region Helpers

        private void PopulateEditForm(ConnectionConfig config)
        {
            EditNameBox.Text = config.Name;
            EditHostBox.Text = config.Host;
            EditPortBox.Value = config.Port;
            EditUsernameBox.Text = config.Username;
            EditPasswordBox.Password = ""; // Don't show stored encrypted password
            EditKeyPathBox.Text = config.PrivateKeyPath;
            EditLocalPathBox.Text = config.LocalDefaultPath;
            EditRemotePathBox.Text = config.RemoteDefaultPath;
            EditProtocolCombo.SelectedIndex = (int)config.Protocol;
            EditAuthTypeCombo.SelectedIndex = (int)config.AuthType;
            EditFavoriteCheck.IsChecked = config.IsFavorite;
            EditNotesBox.Text = config.Notes ?? "";

            // Show hint about stored password
            if (!string.IsNullOrEmpty(config.EncryptedPassword))
            {
                EditPasswordBox.PlaceholderText = "已保存（如需更改请输入新密码）";
            }
            else
            {
                EditPasswordBox.PlaceholderText = "密码（保存时加密存储）";
            }
        }

        private void ReadEditForm()
        {
            if (_viewModel.SelectedConnection == null) return;

            _viewModel.EditName = EditNameBox.Text ?? "";
            _viewModel.EditHost = EditHostBox.Text ?? "";
            _viewModel.EditPort = (int)EditPortBox.Value;
            _viewModel.EditUsername = EditUsernameBox.Text ?? "";

            // Pass plaintext password to ViewModel
            // The ViewModel's SaveConnectionAsync handles encryption
            var enteredPassword = EditPasswordBox.Password ?? "";
            if (!string.IsNullOrEmpty(enteredPassword))
            {
                _viewModel.EditPassword = enteredPassword;
            }
            else
            {
                // Keep existing: ViewModel.PopulateEditForm already decrypted it
                // So EditPassword holds the plaintext, re-save will re-encrypt
            }

            _viewModel.EditPrivateKeyPath = EditKeyPathBox.Text ?? "";
            _viewModel.EditLocalPath = EditLocalPathBox.Text ?? "";
            _viewModel.EditRemotePath = EditRemotePathBox.Text ?? "/";
            _viewModel.EditProtocol = (ProtocolType)EditProtocolCombo.SelectedIndex;
            _viewModel.EditAuthType = (AuthenticationType)EditAuthTypeCombo.SelectedIndex;
            _viewModel.EditIsFavorite = EditFavoriteCheck.IsChecked == true;
            _viewModel.EditNotes = EditNotesBox.Text ?? "";
        }

        private void ClearEditForm()
        {
            EditNameBox.Text = "";
            EditHostBox.Text = "";
            EditPortBox.Value = 22;
            EditUsernameBox.Text = "";
            EditPasswordBox.Password = "";
            EditPasswordBox.PlaceholderText = "密码（保存时加密存储）";
            EditKeyPathBox.Text = "";
            EditLocalPathBox.Text = "";
            EditRemotePathBox.Text = "/";
            EditProtocolCombo.SelectedIndex = 0;
            EditAuthTypeCombo.SelectedIndex = 0;
            EditFavoriteCheck.IsChecked = false;
            EditNotesBox.Text = "";
            StatusText.Text = "";
        }

        private void ShowEditForm()
        {
            EditPanel.Visibility = Visibility.Visible;
            PlaceholderPanel.Visibility = Visibility.Collapsed;
        }

        private void HideEditForm()
        {
            EditPanel.Visibility = Visibility.Collapsed;
            PlaceholderPanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Converts bool to Visibility for XAML bindings.
        /// </summary>
        public static Visibility BoolToVisibility(bool value)
            => value ? Visibility.Visible : Visibility.Collapsed;

        #endregion
    }
}