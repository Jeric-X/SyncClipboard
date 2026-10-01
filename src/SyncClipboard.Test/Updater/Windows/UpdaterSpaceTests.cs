using SyncClipboard.Updater;
using System.Diagnostics;
using System.Security.Principal;

namespace SyncClipboard.Test.Updater.Windows;

[TestClass]
[TestCategory("PlatformWindows")]
public class UpdaterSpaceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void UnknownUncVolume_DoesNotBypassSpaceCheck()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows UNC path test.");
            return;
        }
        Assert.Throws<IOException>(() => PackageFiles.CheckSpace(
            @"\\127.0.0.1\SyncClipboard-unknown-share\installation", 1));
    }

    [TestMethod]
    public async Task ReachableUncVolume_ReportsFreeSpaceAndAccessErrors()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows UNC path test.");
            return;
        }
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            Assert.Inconclusive("Creating a temporary SMB share requires administrator privileges.");
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-share-").FullName;
        var share = "SyncClipboard-test-" + Guid.NewGuid().ToString("N");
        var shared = false;
        try
        {
            await ShareCommandAsync(share + "=" + directory);
            shared = true;
            var root = @"\\localhost\" + share;
            PackageFiles.CheckSpace(root, 1);
            PackageFiles.CheckSpace(root + "\\", 1);
            var nested = Directory.CreateDirectory(Path.Combine(directory, "nested")).Name;
            PackageFiles.CheckSpace(Path.Combine(root, nested), 1);
            Assert.Throws<IOException>(() => PackageFiles.CheckSpace(root, long.MaxValue - (64L * 1024 * 1024)));
            var error = Assert.Throws<IOException>(() => PackageFiles.CheckSpace(Path.Combine(root, "missing"), 1));
            Assert.IsNotNull(error.InnerException);
        }
        finally
        {
            try
            {
                if (shared)
                    await ShareCommandAsync(share, "/delete", "/y");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private async Task ShareCommandAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("net.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("share");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        var error = process.StandardError.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(0, process.ExitCode, await output + await error);
    }
}
