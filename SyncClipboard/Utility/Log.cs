using System;
using System.IO;

namespace SyncClipboard.Utility
{
    static class Log
    {
        public static void Write(string str)
        {
            string logStr = string.Format("[{0}] {1}", DateTime.Now, str);

#if DEBUG
            WriteToConsole(logStr);
#else
            WriteToFile(logStr);
#endif
        }

        private static void WriteToFile(string logStr)
        {
            //判断文件夹是否存在
            if (!Directory.Exists(Program.InternalFolder))
            {
                Directory.CreateDirectory(Program.InternalFolder);
            }

            try
            {


                using (StreamWriter file = new StreamWriter($@"{Program.InternalFolder}\Log.txt", true, System.Text.Encoding.UTF8))
                {
                    file.WriteLine(logStr);
                    file.WriteLine();// 直接追加文件末尾，换行
                    file.Close();
                }
            }
            catch (Exception)
            {
                //throw;
            }
        }

        private static void WriteToConsole(string logStr)
        {
            Console.WriteLine(logStr);
        }
    }
}
