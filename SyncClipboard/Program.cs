using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SyncClipboard
{
    static class Program
    {
        public static String softName = "SyncClipboard";
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            System.Threading.Mutex mutex = new System.Threading.Mutex(true, Program.softName);
            if (mutex.WaitOne(0, false))
            {
                Application.Run(new MainForm());
            }
        }
    }
}
