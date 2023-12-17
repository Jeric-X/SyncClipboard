using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SyncClipboard.Core;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using System;

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
        public ILogger Logger { get; private set; }

        private readonly ProgramWorkflow ProgramWorkflow;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            UnhandledException += App_UnhandledException;

            Services = AppServices.ConfigureServices().BuildServiceProvider();
            Logger = Services.GetRequiredService<ILogger>();
            ProgramWorkflow = new ProgramWorkflow(Services);

            var theme = Services.GetRequiredService<ConfigManager>().GetConfig<ProgramConfig>().Theme;
            if (StringToTheme(theme) is ApplicationTheme applicationTheme)
            {
                RequestedTheme = applicationTheme;
            }

            this.InitializeComponent();
        }

        internal void ExitApp()
        {
            ProgramWorkflow.Stop();
            UnhandledException -= App_UnhandledException;
            Environment.Exit(0);
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Logger?.Write("UnhandledException" + e.Message);
        }

        private static ApplicationTheme? StringToTheme(string theme) => theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => null,
        };

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            Logger.Write("App started");
            ProgramWorkflow.Run();
        }
    }
}