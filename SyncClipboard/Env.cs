using System.IO;

namespace SyncClipboard
{
    internal static class Env
    {
        public const string SoftName = "SyncClipboard";
        public const string VERSION = "1.3.11";
        internal static readonly string Directory = System.Windows.Forms.Application.StartupPath;
        internal static readonly string LOCAL_FILE_FOLDER = FullPath("file");
        internal static string FullPath(string relativePath)
        {
            return Path.Combine(Directory, relativePath);
        }
    }
}