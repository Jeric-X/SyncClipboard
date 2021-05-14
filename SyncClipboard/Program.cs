using System;
using System.Threading;
using System.Windows.Forms;
using SyncClipboard.Control;
using SyncClipboard.Utility;
using SyncClipboard.Service;
using SyncClipboard.Module;
namespace SyncClipboard
{
    internal static class Program
    {
        public static string SoftName = "SyncClipboard";
        public static MainController mainController;

        private static readonly ServiceManager _serviceManager = new ServiceManager();
        public static WebDav webDav;
        public static Notifyer notifyer;

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Log.Write("[Program] started");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Mutex mutex = new Mutex(false, SoftName, out bool creetedNew);
            if (creetedNew)
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                // handle UI exceptions
                Application.ThreadException += Application_ThreadException;
                // handle non-UI exceptions
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                Application.ApplicationExit += Application_ApplicationExit;

                StartUp();
                mainController = new MainController();
                notifyer = mainController.Notifyer;
                _serviceManager.StartUpAllService();

                Application.Run();
            }
            else
            {
                MessageBox.Show("已经存在一个正在运行中的实例！", SoftName);
            }
        }

        private static void StartUp()
        {
            LoadUserConfig();
            LoadGlobal();
        }

        private static void LoadGlobal()
        {
            webDav = new WebDav(
                UserConfig.Config.SyncService.RemoteURL,
                UserConfig.Config.SyncService.UserName,
                UserConfig.Config.SyncService.Password
            );

            webDav.TestAliveAsync().ContinueWith(
                (res) => Log.Write(res.Result.ToString()),
                System.Threading.Tasks.TaskContinuationOptions.NotOnFaulted
            );
        }

        private static void ConfigChangedHandler()
        {
            mainController.LoadConfig();
            LoadGlobal();
            _serviceManager.LoadAllService();
        }

        private static void LoadUserConfig()
        {
            UserConfig.Load();
            UserConfig.ConfigChanged += ConfigChangedHandler;
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Log.Write("未知错误:" + e.Exception.Message);
        }
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Write("未知错误:" + e.ExceptionObject.ToString());
        }
        private static void Application_ApplicationExit(object sender, EventArgs e)
        {
            _serviceManager?.StopAllService();
            Application.ApplicationExit -= Application_ApplicationExit;
            Application.ThreadException -= Application_ThreadException;
            Log.Write("[Program] exited");
        }
    }
}
