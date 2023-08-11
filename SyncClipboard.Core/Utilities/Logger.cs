using Microsoft.Extensions.Options;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Diagnostics;

namespace SyncClipboard.Core.Utilities
{
    public class Logger : ILogger
    {
        private readonly string LOG_FOLDER;
        private static readonly object LOCKER = new();

        public Logger(IOptions<LoggerOption> option)
        {
            LOG_FOLDER = option.Value.Path ?? throw new ArgumentNullException(nameof(option.Value.Path), "日志路径为null"); ;
        }

        public void Write(string? tag, string str, StackFrame? stackFrame = null)
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

            WriteToConsole(logStr);
            string logFile = dayTime.ToString("yyyyMMdd");
            WriteToFile(logStr, logFile);
        }

        void ILogger.Write(string str)
        {
            Write(null, str, new StackTrace(true).GetFrame(1));
        }

        void ILogger.Write(string? tag, string str)
        {
            Write(tag, str, new StackTrace(true).GetFrame(1));
        }

        private void WriteToFile(string logStr, string logFile)
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
                    using StreamWriter file = new(Path.Combine(LOG_FOLDER, $"{logFile}.txt"), true, System.Text.Encoding.UTF8);
                    file.WriteLine(logStr);
                }
                catch
                {
                    Console.WriteLine(logStr);
                }
            }
        }

        [Conditional("DEBUG")]
        private static void WriteToConsole(string logStr)
        {
            Task.Run(() => Trace.WriteLine(logStr));
        }
    }
}