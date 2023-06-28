using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SyncClipboard.Core;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.WinUI3.Views;
using System;
using System.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public new static App Current => (App)Application.Current;
        public IServiceProvider Services { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            Services = ConfigureServices();
            UnhandledException += App_UnhandledException;
            this.InitializeComponent();
        }

        private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Trace.WriteLine(e.Message);
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            ProgramWorkflow.ConfigCommonService(services);

            services.AddSingleton<SettingWindow>();
            services.AddSingleton<ITrayIcon, TrayIcon>(
                (sp) => new TrayIcon
                {
                    DoubleClickCommand = new RelayCommand(() => sp.GetService<SettingWindow>()?.Activate())
                });
            services.AddSingleton<IContextMenu, TrayIconContextMenu>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            new ProgramWorkflow(Services).Run();
        }
    }
}