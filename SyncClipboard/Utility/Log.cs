﻿using System;
using System.Diagnostics;
using System.IO;

namespace SyncClipboard.Utility
{
    internal static class Log
    {
        private static readonly string LOG_FOLDER = Env.FullPath("Log");
        private static readonly object LOCKER = new object();

        public static void Write(string str)
        {
            StackFrame sf = new StackTrace(true).GetFrame(1);

            var dayTime = DateTime.Now;
            var fileName = Path.GetFileName(sf.GetFileName());
            var lineNumber = sf.GetFileLineNumber();

            string logStr = string.Format("[{0}][{1, -20}][{2, 4}] {3}", dayTime.ToString("yyyy/MM/dd HH:mm:ss"), fileName, lineNumber, str);

#if DEBUG
            WriteToConsole(logStr);
#else
            string logFile = dayTime.ToString("yyyyMMdd");
            WriteToFile(logStr, logFile);
#endif
        }

        private static void WriteToFile(string logStr, string logFile)
        {
            //判断文件夹是否存在
            if (!Directory.Exists(LOG_FOLDER))
            {
                Directory.CreateDirectory(LOG_FOLDER);
            }

            lock (LOCKER)
            {
                try
                {
                    using (StreamWriter file = new StreamWriter($@"{LOG_FOLDER}\{logFile}.txt", true, System.Text.Encoding.UTF8))
                    {
                        file.WriteLine(logStr);
                    }
                }
                catch
                {
                    throw;
                }
            }
        }

        private static void WriteToConsole(string logStr)
        {
            Console.WriteLine(logStr);
        }
    }
}
