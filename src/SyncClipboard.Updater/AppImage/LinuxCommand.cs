using System.ComponentModel;
using System.Diagnostics;

namespace SyncClipboard.Updater.AppImage;

internal static class LinuxCommand
{
    public static async Task RunElevatedAsync(string executable, string[] arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo("/usr/bin/pkexec")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(executable);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        try
        {
            using var process = Process.Start(start) ?? throw new IOException(UpdaterText.Current.ElevatedStartFailed);
            var output = process.StandardOutput.ReadToEndAsync(token);
            var errors = process.StandardError.ReadToEndAsync(token);
            try
            {
                await process.WaitForExitAsync(token);
            }
            catch (OperationCanceledException)
            {
                // Wait for a privileged operation to stop before recovery can touch the same file.
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }
            var message = await errors;
            var result = await output;
            if (process.ExitCode != 0)
                throw new IOException($"{UpdaterText.Current.CommandFailed}{executable} ({process.ExitCode}): {message.Trim()} {result.Trim()}");
        }
        catch (Win32Exception error)
        {
            throw new IOException(UpdaterText.Current.ElevatedStartFailed + " /usr/bin/pkexec", error);
        }
    }
}
