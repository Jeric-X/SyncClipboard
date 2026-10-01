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
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        var message = await errors;
        var result = await output;
        if (process.ExitCode != 0)
            throw new IOException($"{UpdaterText.Current.CommandFailed}{executable} ({process.ExitCode}): {message.Trim()} {result}");
        return result.Trim();
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
}
