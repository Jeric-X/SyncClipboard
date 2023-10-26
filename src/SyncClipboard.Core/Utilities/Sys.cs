using System.Diagnostics;

namespace SyncClipboard.Core.Utilities
{
    public static class Sys
    {
        public static Process? OpenWithDefaultApp(string? arg)
        {
            System.ArgumentNullException.ThrowIfNull(arg);
            return Process.Start(new ProcessStartInfo(arg) { UseShellExecute = true });
        }
    }
}