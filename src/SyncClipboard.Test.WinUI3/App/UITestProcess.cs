using System.Diagnostics;

namespace SyncClipboard.Test.WinUI3.App;

internal static class UITestProcess
{
    internal static async Task RunAsync(Func<Task> test, CancellationToken token)
    {
        var method = test.Method;
        if (!method.IsStatic || method.DeclaringType?.Assembly != typeof(UITestProcess).Assembly)
            throw new ArgumentException("UI checks must be static methods in the WinUI test assembly.", nameof(test));

        var start = new ProcessStartInfo(Path.ChangeExtension(typeof(UITestProcess).Assembly.Location, ".exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(method.DeclaringType.FullName!);
        start.ArgumentList.Add(method.Name);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(token);
        var errors = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(60), token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        Assert.AreEqual(0, process.ExitCode, await output + await errors);
    }
}
