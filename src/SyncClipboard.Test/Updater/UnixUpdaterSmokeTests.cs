using SyncClipboard.Core.Utilities.Updater;
using System.Diagnostics;

namespace SyncClipboard.Test.Updater;

[TestClass]
[TestCategory("PlatformMacOS")]
[TestCategory("PlatformLinux")]
[TestCategory("UpdaterSmoke")]
public class UnixUpdaterSmokeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PublishedUpdater_ShowsWindowUsingMainApplicationDependencies()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Requires macOS or Linux.");
        var source = Environment.GetEnvironmentVariable("SYNC_CLIPBOARD_UPDATER_TEST_SOURCE");
        if (string.IsNullOrEmpty(source))
            Assert.Inconclusive("Set SYNC_CLIPBOARD_UPDATER_TEST_SOURCE to the main application bundle or output directory.");
        var token = TestContext.CancellationTokenSource.Token;
        var workspace = OperatingSystem.IsMacOS()
            ? await FileReplacementPackageInstaller.PrepareMacUpdaterAsync(source, token)
            : await FileReplacementPackageInstaller.PrepareUnixUpdaterAsync(FileReplacementPackageInstaller.LinuxUpdaterFiles.GetFiles(source), token);
        var workingDirectory = Directory.CreateTempSubdirectory("SyncClipboard smoke 中文 ' ").FullName;
        try
        {
            var start = new ProcessStartInfo(Path.Combine(workspace, "SyncClipboard.Updater"))
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("--smoke-test");
            if (OperatingSystem.IsLinux())
                FileReplacementPackageInstaller.LinuxUpdaterFiles.ConfigureStartInfo(start);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(token);
            var errors = process.StandardError.ReadToEndAsync(token);
            try
            {
                await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
                TestContext.WriteLine(await output);
                TestContext.WriteLine(await errors);
                Assert.AreEqual(0, process.ExitCode, await errors);
                Assert.Contains("GUI_SMOKE=PASS", await output);
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
            Directory.Delete(workingDirectory, true);
        }
    }
}
