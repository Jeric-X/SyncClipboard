using System.ComponentModel;
using System.Diagnostics;

namespace SyncClipboard.Updater.Dmg;

internal static class MacCommand
{
    public static async Task<string> RunAsync(string executable, string[] arguments, CancellationToken token, bool elevated = false)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        if (elevated)
        {
            start.FileName = "/usr/bin/osascript";
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("on run argv\nreturn do shell script (item 1 of argv) with administrator privileges\nend run");
            start.ArgumentList.Add(string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote)));
        }
        else
        {
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new IOException(UpdaterText.Current.CommandFailed + executable);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(process);
            throw;
        }
        var message = await errors;
        var result = await output;
        if (process.ExitCode != 0)
            throw new IOException($"{UpdaterText.Current.CommandFailed}{executable} ({process.ExitCode}): {message.Trim()} {result}");
        return result.Trim();
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or AggregateException)
        {
            Debug.WriteLine(error);
        }
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException error)
        {
            // Continue cancellation even if a privileged child could not be stopped.
            Debug.WriteLine(error);
        }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
}
