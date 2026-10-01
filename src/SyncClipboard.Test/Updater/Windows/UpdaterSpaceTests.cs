using SyncClipboard.Updater;

namespace SyncClipboard.Test.Updater.Windows;

[TestClass]
[TestCategory("PlatformWindows")]
public class UpdaterSpaceTests
{
    [TestMethod]
    public void UnknownUncVolume_DoesNotBypassSpaceCheck()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows UNC path test.");
        Assert.Throws<IOException>(() => PackageFiles.CheckSpace(
            @"\\127.0.0.1\SyncClipboard-unknown-share\installation", 1));
    }
}
