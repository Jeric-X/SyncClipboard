using System;
using System.Threading;
using System.Windows.Forms;
using SyncClipboard.Control;
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

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
                
            bool creetedNew;
            System.Threading.Mutex mutex = new System.Threading.Mutex(false, Program.SoftName, out creetedNew);
            if (creetedNew)
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                // handle UI exceptions
                Application.ThreadException += Application_ThreadException;
                // handle non-UI exceptions
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                Application.ApplicationExit += Application_ApplicationExit;

                Config.Load();
                mainController = new MainController();
                ClipboardListener = new ClipboardListener();

                pullService = new PullService(mainController.GetNotifyFunction());
                pushService = new PushService(mainController.GetNotifyFunction());

                Application.Run();
            }
            else
            {
                MessageBox.Show("已经存在一个正在运行中的实例！", SoftName);
            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            mainController.setLog(true, false, "未知错误", e.Exception.Message.ToString(), null, "erro");
        }
         private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            mainController.setLog(true, false, "未知错误", e.ExceptionObject.ToString(), null, "erro");
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
            Application.ApplicationExit -= Application_ApplicationExit;
            Application.ThreadException -= Application_ThreadException;
        }
    }
}
