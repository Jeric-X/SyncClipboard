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
        private static PullService syncService;
        private static PushService pushService;

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
                
            System.Threading.Mutex mutex = new System.Threading.Mutex(true, Program.SoftName);
            if (mutex.WaitOne(0, false))
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                // handle UI exceptions
                Application.ThreadException += Application_ThreadException;
                // handle non-UI exceptions
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                Application.ApplicationExit += Application_ApplicationExit;

                Config.Load();
                mainController = new MainController();
                syncService = new PullService(mainController.GetNotifyFunction());
                pushService = new PushService(mainController.GetNotifyFunction());

                Application.Run();
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
            if (syncService != null)
            {
                syncService.Stop();
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
