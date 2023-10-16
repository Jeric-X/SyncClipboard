using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SyncClipboard.Core;
using SyncClipboard.Core.Interfaces;
using System;
using System.Diagnostics;
using System.Threading;

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

        private ProgramWorkflow ProgramWorkflow;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
        public App()
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
        {
            UnhandledException += App_UnhandledException;
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

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            Services = AppServices.ConfigureServices().BuildServiceProvider();
            Logger = Services.GetRequiredService<ILogger>();
            Logger?.Write("App started");
            ProgramWorkflow = new ProgramWorkflow(Services);
            ProgramWorkflow.Run();
            MenuItem[] menuItems = { new MenuItem(Core.I18n.Strings.Exit, ExitApp) };
            Services.GetService<IContextMenu>()?.AddMenuItemGroup(menuItems);
        }
    }
}