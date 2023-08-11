using System.IO;

namespace SyncClipboard
{
    internal static class Env
    {
        public const string SoftName = "SyncClipboard";
        internal static readonly string Directory = System.Windows.Forms.Application.StartupPath;
        internal static string FullPath(string relativePath)
        {
            return Path.Combine(Directory, relativePath);
        }
    }
}