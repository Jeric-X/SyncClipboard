using System.Diagnostics;

namespace SyncClipboard.Utility
{
    public static class Sys
    {
        #nullable enable
        public static Process? OpenWithDefaultApp(string arg)
        {
            return Process.Start(new ProcessStartInfo(arg) { UseShellExecute = true });
        }
    }
}