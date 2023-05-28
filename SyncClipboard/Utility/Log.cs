using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
#nullable enable

namespace SyncClipboard.Utility
{
    internal static class Log
    {
        private static readonly string LOG_FOLDER = Env.LOCAL_LOG_FOLDER;
        private static readonly object LOCKER = new();

        public static void Write(string? tag, string str, StackFrame? stackFrame = null)
        {
            StackFrame? sf = stackFrame ?? new StackTrace(true).GetFrame(1);

            var dayTime = DateTime.Now;
            var fileName = Path.GetFileName(sf?.GetFileName());
            var lineNumber = sf?.GetFileLineNumber();

            string logStr;
            if (tag is not null)
            {
                logStr = string.Format("[{0}][{1, -20}][{2, 4}][{4}] {3}",
                    dayTime.ToString("yyyy/MM/dd HH:mm:ss"), fileName, lineNumber, str, tag);
            }
            else
            {
                logStr = string.Format("[{0}][{1, -20}][{2, 4}] {3}",
                    dayTime.ToString("yyyy/MM/dd HH:mm:ss"), fileName, lineNumber, str);
            }

#if DEBUG
            WriteToConsole(logStr);
#else
            string logFile = dayTime.ToString("yyyyMMdd");
            WriteToFile(logStr, logFile);
#endif
        }

        public static void Write(string str)
        {
            Write(null, str, new StackTrace(true).GetFrame(1));
        }

        public static void WriteToFile(string logStr, string logFile)
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
                    using StreamWriter file = new($@"{LOG_FOLDER}\{logFile}.txt", true, System.Text.Encoding.UTF8);
                    file.WriteLine(logStr);
                }
                catch
                {
                    Console.WriteLine(logStr);
                }
            }
        }

        private static void WriteToConsole(string logStr)
        {
            Task.Run(() => Trace.WriteLine(logStr));
        }
    }
}
