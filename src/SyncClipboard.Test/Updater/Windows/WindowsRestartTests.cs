using SyncClipboard.Core.Commons;
using SyncClipboard.Updater;
using SyncClipboard.Updater.Zip;
using System.Diagnostics;
using System.Reflection;
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
            using System.Security.Principal;

            class Recorder
            {
                static void Main(string[] args)
                {
                    using (var identity = WindowsIdentity.GetCurrent())
                    {
                        var result = new string[args.Length + 3];
                        result[0] = Process.GetCurrentProcess().Id.ToString();
                        result[1] = Environment.CurrentDirectory;
                        result[2] = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator).ToString();
                        Array.Copy(args, 0, result, 3, args.Length);
                        var output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "arguments.txt");
                        File.WriteAllLines(output + ".tmp", result);
                        File.Move(output + ".tmp", output);
                    }
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
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ShellLaunch_PassesArgumentsToChild(bool elevated, bool updateCompleted)
    {
        var administrator = IsAdministrator();
        if (elevated && !administrator)
            Assert.Inconclusive("Run this test as administrator to exercise runas without an interactive UAC prompt.");

        InvokeLauncher("StartWithShell", Executable, target, elevated, CompletionArgument(updateCompleted));
        await AssertChildAsync(updateCompleted, administrator);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TokenLaunch_PassesArgumentsToChild(bool updateCompleted)
    {
        if (!IsAdministrator())
            Assert.Inconclusive("CreateProcessWithTokenW requires an administrator test host.");

        // Exercise the production native-token backend even on CI without a non-elevated desktop shell.
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.Duplicate | TokenAccessLevels.AssignPrimary);
        InvokeLauncher("StartWithToken", Executable, target, CompletionArgument(updateCompleted), identity.AccessToken);
        await AssertChildAsync(updateCompleted, expectedAdministrator: true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Restart_PropagatesCompletionStatus(bool updateCompleted)
    {
        var administrator = IsAdministrator();
        await UpdateWorker.RestartAsync(Arguments() with { AppElevated = administrator }, updateCompleted);
        await AssertChildAsync(updateCompleted, administrator);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RestartAsDesktopUser_PassesArgumentsWithoutAdministratorRights(bool updateCompleted)
    {
        if (!IsAdministrator())
            Assert.Inconclusive("Requires an elevated test host and a non-elevated desktop shell.");
        try
        {
            await UpdateWorker.RestartAsync(Arguments() with { AppElevated = false }, updateCompleted);
        }
        catch (IOException error) when (error.Message == UpdaterText.Current.DesktopUserUnavailable)
        {
            Assert.Inconclusive("No non-elevated desktop shell is available. The native-token backend is tested separately.");
        }
        await AssertChildAsync(updateCompleted, expectedAdministrator: false);
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string? CompletionArgument(bool completed) => completed ? StartArguments.UpdateCompleted : null;

    private static void InvokeLauncher(string method, params object?[] arguments)
        => typeof(WindowsProcessLauncher).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments);

    private async Task AssertChildAsync(bool updateCompleted, bool expectedAdministrator)
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
        Assert.AreEqual(target, result[1], ignoreCase: true);
        Assert.AreEqual(expectedAdministrator, bool.Parse(result[2]));
        string[] expected = updateCompleted ? [StartArguments.UpdateCompleted] : [];
        CollectionAssert.AreEqual(expected, result[3..]);
    }
}
