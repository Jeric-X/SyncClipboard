using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SyncClipboard
{
    static class Program
    {
        public static String SoftName = "SyncClipboard";
        public static String DefaultServer = "https://cloud.jericx.xyz/remote.php/dav/files/Clipboard/Clipboard/";
        public static String DefaultUser = "Clipboard";
        public static String DefaultPassword = "Clipboard";
        public static MainForm mainForm;

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
                mainForm = new MainForm();
                Application.Run(mainForm);
            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            mainForm.setLog(true,false,"未知错误",e.Exception.Message.ToString(),null,"erro");
        }
         private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            mainForm.setLog(true, false, "未知错误", e.ExceptionObject.ToString(), null, "erro");
        }
        private static void Application_ApplicationExit(object sender, EventArgs e)
        {
            // detach static event handlers
            Application.ApplicationExit -= Application_ApplicationExit;
            Application.ThreadException -= Application_ThreadException;
        }
    }
}
