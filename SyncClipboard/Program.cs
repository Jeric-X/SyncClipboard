using System;
using System.Threading;
using System.Windows.Forms;
using SyncClipboard.Control;
using SyncClipboard.Utility;
using SyncClipboard.Service;
namespace SyncClipboard
{
    static class Program
    {
        public static String SoftName = "SyncClipboard";
        public static String DefaultServer = "https://file.jericx.xyz/remote.php/dav/files/Clipboard/Clipboard/";
        public static String DefaultUser = "Clipboard";
        public static String DefaultPassword = "Clipboard";
        public static MainController mainController;
        public static ClipboardListener ClipboardListener;

        public static PullService pullService;
        public static PushService pushService;
        public static CommandService commandService;
        public static WebDav webDav;
        public static Notifyer notifyer;

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Utility.Log.Write("[Program] started");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            System.Threading.Mutex mutex = new System.Threading.Mutex(false, Program.SoftName, out bool creetedNew);
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
                ClipboardListener = new ClipboardListener();

                pushService = new PushService(mainController.Notifyer);
                pullService = new PullService(pushService, mainController.Notifyer);
                commandService = new CommandService();
                commandService.Start();

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
                (res) =>
                {
                    Log.Write(res.Result.ToString());
                },
                System.Threading.Tasks.TaskContinuationOptions.NotOnFaulted
            );

        }

        private static void ConfigChangedHandler()
        {
            Program.pullService.Load();
            Program.pushService.Load();
            Program.commandService.Load();
            Program.mainController.LoadConfig();
            LoadGlobal();
        }

        private static void LoadUserConfig()
        {
            UserConfig.Load();
            UserConfig.ConfigChanged += ConfigChangedHandler;
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Log.Write("未知错误:" + e.Exception.Message.ToString());
        }
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Write("未知错误:" + e.ExceptionObject.ToString());
        }
        private static void Application_ApplicationExit(object sender, EventArgs e)
        {
            if (pullService != null)
            {
                pullService.Stop();
            }
            if (pushService != null)
            {
                pushService.Stop();
            }
            if (commandService != null)
            {
                commandService.Stop();
            }
            Application.ApplicationExit -= Application_ApplicationExit;
            Application.ThreadException -= Application_ThreadException;
            Utility.Log.Write("[Program] exited");
        }
    }
}
