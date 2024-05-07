using System.Diagnostics;

namespace SyncClipboard.Core.Utilities
{
    public static class Sys
    {
        public static Process? OpenWithDefaultApp(string? arg)
        {
            ArgumentNullException.ThrowIfNull(arg);
            return Process.Start(new ProcessStartInfo(arg) { UseShellExecute = true });
        }

#if WINDOWS
        public static void OpenFileInExplorer(string path)
        {
            var open = new Process();
            open.StartInfo.FileName = "explorer";
            open.StartInfo.Arguments = "/e,/select," + path;
            open.Start();
        }

        public static void OpenFolderInExplorer(string path)
        {
            var open = new Process();
            open.StartInfo.FileName = "explorer";
            open.StartInfo.Arguments = path;
            open.Start();
        }
#endif
    }
}