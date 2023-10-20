using SyncClipboard.Core.Utilities;
using System;
using System.Threading;
using System.Windows.Forms;
namespace SyncClipboard
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.SetCompatibleTextRenderingDefault(false);

            using var _ = new Mutex(false, Env.AppId, out bool createdNew);
            if (createdNew)
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                // handle UI exceptions
                Application.ThreadException += Application_ThreadException;
                // handle non-UI exceptions
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                Application.ApplicationExit += Application_ApplicationExit;

                Global.StartUp();

                Application.Run();
            }
            else
            {
                AppInstance.ActiveOtherInstance(Env.AppId).Wait();
                MessageBox.Show("已经存在一个正在运行中的实例！", Env.SoftName);
            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Global.Logger?.Write("未知错误:" + e.Exception.Message);
        }
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Global.Logger?.Write("未知错误:" + e.ExceptionObject.ToString());
        }
        private static void Application_ApplicationExit(object sender, EventArgs e)
        {
            Global.EndUp();
            Application.ApplicationExit -= Application_ApplicationExit;
            Application.ThreadException -= Application_ThreadException;
            Global.Logger?.Write("[Program] exited");
        }
    }
}
