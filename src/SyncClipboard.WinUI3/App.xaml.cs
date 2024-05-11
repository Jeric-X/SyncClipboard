using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SyncClipboard.Core;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.DbgHelp;

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
        public AppCore AppCore { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            UnhandledException += App_UnhandledException;

            Services = AppServices.ConfigureServices().BuildServiceProvider();
            Logger = Services.GetRequiredService<ILogger>();
            AppCore = new AppCore(Services);

            this.InitializeComponent();
        }

        internal void ExitApp()
        {
            AppCore.Stop();
            UnhandledException -= App_UnhandledException;
            Environment.Exit(0);
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogUnhandledException(e.Exception);
        }

        public void LogUnhandledException(Exception e)
        {
            Logger.Write($"UnhandledException {e.GetType()} {e.Message} \n{e.StackTrace}");
            Logger.Flush();

            var mdei = new MINIDUMP_EXCEPTION_INFORMATION
            {
                ThreadId = Kernel32.GetCurrentThreadId(),
                ExceptionPointers = Marshal.GetExceptionPointers()
            };

            var path = Path.Combine(Core.Commons.Env.LogFolder, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss}.dmp");
            using FileStream fs = new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Write);
            using Process process = Process.GetCurrentProcess();
            MiniDumpWriteDump(process, (uint)process.Id, fs.SafeFileHandle, MINIDUMP_TYPE.MiniDumpNormal, mdei, default);
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            Logger.Write("App started");
            AppCore.Run();
        }
    }
}