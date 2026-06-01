using Hyper_Transmit.Services;
using Hyper_Transmit.Services.Interfaces;
using Hyper_Transmit.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace Hyper_Transmit
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// The main application window instance.
        /// </summary>
        public static MainWindow? MainWindow { get; private set; }

        /// <summary>
        /// Application-wide DI host.
        /// </summary>
        public static IHost Host { get; private set; } = null!;

        /// <summary>
        /// Service provider for resolving dependencies.
        /// </summary>
        public static IServiceProvider Services => Host.Services;

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            InitializeComponent();

            // Handle unhandled exceptions from background threads (e.g., SSH.NET)
            UnhandledException += (sender, args) =>
            {
                args.Handled = true;
                System.Diagnostics.Debug.WriteLine($"Unhandled exception: {args.Exception}");
            };

            // Also catch .NET runtime unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"AppDomain unhandled: {args.ExceptionObject}");
            };

            // Catch unobserved task exceptions
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                args.SetObserved();
                System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {args.Exception}");
            };

            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Services (Singleton)
                    services.AddSingleton<CredentialService>();
                    services.AddSingleton<Logger>();
                    services.AddSingleton<ISshService, SshService>();
                    services.AddSingleton<ILocalFileService, LocalFileService>();
                    services.AddSingleton<ITransferQueueService, TransferQueueService>();
                    services.AddSingleton<ISessionManager, SessionManager>();
                    services.AddSingleton<ISettingsService, SettingsService>();

                    // ViewModels (Transient - new instance per request)
                    services.AddTransient<MainWindowViewModel>();
                    services.AddTransient<HomePageViewModel>();
                    services.AddTransient<ConnectionManagerViewModel>();
                    services.AddTransient<TransferQueueViewModel>();
                    services.AddTransient<SettingsViewModel>();

                    // Views
                    services.AddTransient<MainWindow>();
                })
                .Build();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Set dispatcher for TransferTask to marshal PropertyChanged to UI thread
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            if (dispatcher != null)
            {
                Models.TransferTask.SetDispatcher(dispatcher);
            }

            _window = Services.GetRequiredService<MainWindow>();
            MainWindow = _window as MainWindow;
            _window.Activate();
        }
    }
}