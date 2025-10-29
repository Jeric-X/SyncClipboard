using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SyncClipboard.Core;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.WinUI3.Views;
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
        public MainWindow MainWindow => (MainWindow)Services.GetRequiredService<IMainWindow>();
        public AppCore AppCore { get; private set; }

#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        public App()
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        {
            UnhandledException += App_UnhandledException;
            this.InitializeComponent();
        }

        internal void ExitApp()
        {
            AppCore.Stop();
            UnhandledException -= App_UnhandledException;
            Console.WriteLine("Exited");
            Environment.Exit(0);
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogUnhandledException(e.Exception);
        }

        public void LogUnhandledException(Exception e)
        {
            Console.WriteLine($"UnhandledException {e.GetType()} {e.Message} \n{e.StackTrace}");
            var path = Path.Combine(Core.Commons.Env.LogFolder, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss}.dmp");
            File.WriteAllText(path + ".txt", $"UnhandledException {e.GetType()} {e.Message} \n{e.StackTrace}");

            Logger?.Write($"UnhandledException {e.GetType()} {e.Message} \n{e.StackTrace}");
            Logger?.Flush();

            var mdei = new MINIDUMP_EXCEPTION_INFORMATION
            {
                ThreadId = Kernel32.GetCurrentThreadId(),
                ExceptionPointers = Marshal.GetExceptionPointers()
            };

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
            Services = AppServices.ConfigureServices().BuildServiceProvider();
            Logger = Services.GetRequiredService<ILogger>();
            AppCore = new AppCore(Services);
            Logger.Write("App started");
            AppCore.Run();
        }
    }
}