using SyncClipboard.Core.Commons;
using SyncClipboard.Updater;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace SyncClipboard.Test.Updater.Windows;

[TestClass]
[TestCategory("PlatformWindows")]
[SupportedOSPlatform("windows")]
public class WindowsRestartTests : UpdaterTestBase
{
    [TestInitialize]
    public async Task CreateArgumentRecorder()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Requires Windows process launching.");

        var source = Path.Combine(directory, "Recorder.cs");
        File.WriteAllText(source, """
            using System;
            using System.Diagnostics;
            using System.IO;

            class Recorder
            {
                static void Main(string[] args)
                {
                    var result = new string[args.Length + 2];
                    result[0] = Process.GetCurrentProcess().Id.ToString();
                    result[1] = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version"));
                    Array.Copy(args, 0, result, 2, args.Length);
                    var output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "arguments.txt");
                    File.WriteAllLines(output + ".tmp", result);
                    File.Move(output + ".tmp", output);
                }
            }
            """);
        var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        Assert.IsTrue(File.Exists(compiler), "The Windows fixture requires the .NET Framework C# compiler.");
        var start = new ProcessStartInfo(compiler)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        string[] arguments = ["/nologo", "/target:winexe", "/out:" + Executable, source];
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        var errors = process.StandardError.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(0, process.ExitCode, await output + await errors);
    }

    private string Executable => Path.Combine(target, "SyncClipboard.exe");

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Installation_RestartsUpdatedOrRolledBackApplication(bool rollback)
    {
        File.WriteAllText(Path.Combine(target, "version"), "old");
        File.Copy(Executable, Path.Combine(stage, "SyncClipboard.exe"));
        File.WriteAllText(Path.Combine(stage, "version"), "new");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        Task InstallAsync() => ApplyAsync((phase, percent) =>
        {
            if (rollback && phase == "installing" && percent == 100)
                cancellation.Cancel();
        }, cancellation.Token);
        if (rollback)
            await Assert.ThrowsAsync<OperationCanceledException>(InstallAsync);
        else
            await InstallAsync();

        await UpdateWorker.RestartAsync(Arguments() with { AppElevated = IsAdministrator() }, updateCompleted: !rollback);
        await AssertChildAsync(rollback);
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private async Task AssertChildAsync(bool rollback)
    {
        var output = Path.Combine(target, "arguments.txt");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!File.Exists(output))
        {
            await Task.Delay(50, timeout.Token);
        }
        var result = File.ReadAllLines(output);
        // The marker is written before CLR shutdown; wait before deleting the executable in TestCleanup.
        Process? child = null;
        try
        {
            child = Process.GetProcessById(int.Parse(result[0]));
        }
        catch (ArgumentException)
        {
            // The child already exited.
        }
        using (child)
        {
            if (child is not null)
                await child.WaitForExitAsync(timeout.Token);
        }
        string[] expected = rollback ? ["old"] : ["new", StartArguments.UpdateCompleted];
        CollectionAssert.AreEqual(expected, result[1..]);
    }
}
