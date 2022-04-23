using System.Diagnostics;
#nullable enable

namespace SyncClipboard.Utility
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