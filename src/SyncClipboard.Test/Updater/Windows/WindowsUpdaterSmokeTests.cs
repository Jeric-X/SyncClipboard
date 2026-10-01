using SyncClipboard.Core.Utilities.Updater;
using System.Diagnostics;
using System.Reflection.PortableExecutable;

namespace SyncClipboard.Test.Updater.Windows;

[TestClass]
[TestCategory("PlatformWindows")]
[TestCategory("UpdaterSmoke")]
public class WindowsUpdaterSmokeTests : UpdaterTestBase
{
    [TestMethod]
    public async Task NativeAotUpdater_ShowsWinUiWindowAndDialogUsingMainApplicationDependencies()
    {
        var native = Environment.GetEnvironmentVariable("SYNC_CLIPBOARD_UPDATER_TEST_EXE");
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Requires Windows.");
        if (string.IsNullOrEmpty(native))
            Assert.Inconclusive("Set SYNC_CLIPBOARD_UPDATER_TEST_EXE to the packaged WinUI updater.");
        var sourceDirectory = Path.GetDirectoryName(native)!;
        using (var file = File.OpenRead(native))
        using (var pe = new PEReader(file))
            Assert.IsNull(pe.PEHeaders.CorHeader, "The packaged updater must be a native executable.");
        var workspace = await FileReplacementPackageInstaller.PrepareWindowsUpdaterAsync(sourceDirectory,
            TestContext.CancellationTokenSource.Token);
        try
        {
            var start = new ProcessStartInfo(Path.Combine(workspace, "SyncClipboard.Updater.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("--smoke-test");
            using var process = Process.Start(start)!;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
                var error = process.StandardError.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
                await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token).WaitAsync(TimeSpan.FromSeconds(30), TestContext.CancellationTokenSource.Token);
                Assert.AreEqual(0, process.ExitCode, await error);
                Assert.Contains("GUI_SMOKE=PASS", await output);
                var mode = File.Exists(Path.Combine(sourceDirectory, "Microsoft.WindowsAppRuntime.dll")) ? "local" : "installed";
                Assert.Contains("WINDOWS_APP_SDK=" + mode, await output);
                TestContext.WriteLine(await output);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }
}
