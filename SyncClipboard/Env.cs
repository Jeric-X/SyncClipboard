using System.IO;

namespace SyncClipboard
{
    internal static class Env
    {
        public const string SoftName = "SyncClipboard";
        internal static readonly string Directory = System.Windows.Forms.Application.StartupPath;
        internal static readonly string LOCAL_FILE_FOLDER = FullPath("file");
        internal static readonly string LOCAL_LOG_FOLDER = FullPath("log");
        internal static string FullPath(string relativePath)
        {
            return Path.Combine(Directory, relativePath);
        }
    }
}