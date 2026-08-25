using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Options;
using System.Diagnostics;

namespace SyncClipboard.Core.Utilities
{
    public class Logger(LoggerOption option) : ILogger, IDisposable
    {
        private readonly string LOG_FOLDER = option.Path;
        private readonly LoggerOption _option = option;
        private static readonly object LOCKER = new();
        private StreamWriter? _fileWriter;
        private string? _logFile;

        public void Write(string? tag, string str, StackFrame? stackFrame = null)
        {
            StackFrame? sf = stackFrame ?? new StackTrace(true).GetFrame(1);

            var dayTime = DateTime.Now;
            var fileName = Path.GetFileName(sf?.GetFileName()?.Replace('\\', '/'));
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

        public void Write(string str)
        {
            Write(null, str, new StackTrace(true).GetFrame(1));
        }
        public void Write(string? tag, string str)
        {
            Write(tag, str, new StackTrace(true).GetFrame(1));
        }

        public Task WriteAsync(string str)
        {
            return WriteAsync(null, str);
        }

        public Task WriteAsync(string? tag, string str)
        {
            return Task.Run(() => Write(tag, str, new StackTrace(true).GetFrame(1)));
        }

        public void Flush()
        {
            _fileWriter?.Flush();
        }

        private void WriteToFile(string logStr, string logFile)
        {
            lock (LOCKER)
            {
                try
                {
                    Directory.CreateDirectory(LOG_FOLDER);
                    var fileWriter = GetFileWriter(logFile);
                    fileWriter.WriteLine(logStr);
                    if (_option.FlushImmediately)
                    {
                        fileWriter.Flush();
                    }
                }
                catch
                {
                    Console.WriteLine(logStr);
                }
            }
        }

        private StreamWriter GetFileWriter(string logFile)
        {
            if (_logFile != logFile)
            {
                _fileWriter?.Dispose();
                _fileWriter = null;
                _logFile = logFile;
            }
            _fileWriter ??= new(Path.Combine(LOG_FOLDER, $"{logFile}.txt"), true, System.Text.Encoding.UTF8);
            return _fileWriter;
        }

        [Conditional("DEBUG")]
        private static void WriteToConsole(string logStr)
        {
            Task.Run(() => Trace.WriteLine(logStr));
        }

        ~Logger() => _fileWriter?.Dispose();

        public void Dispose()
        {
            _fileWriter?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
