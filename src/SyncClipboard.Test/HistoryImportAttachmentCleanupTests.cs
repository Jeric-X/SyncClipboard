using SyncClipboard.Core.Utilities.History.HistoryImport;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryImportAttachmentCleanupTests
{
    [TestMethod]
    public void CleanupPreservesNewBindingsAndExternalPaths()
    {
        var temp = Directory.CreateTempSubdirectory("HistoryImportCleanup-");
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(temp.FullName, "record")).FullName;
            var currentDirectory = Directory.CreateDirectory(Path.Combine(root, "current")).FullName;
            var current = Path.Combine(currentDirectory, "keep.txt");
            File.WriteAllText(current, "new");
            var external = Path.Combine(Directory.CreateDirectory(root + "-other").FullName, "keep.txt");
            File.WriteAllText(external, "outside");
            var old = Path.Combine(root, "old.txt");
            File.WriteAllText(old, "corrupt");
            HistoryImportAttachmentCleanup.RemoveObsolete(root, [root, currentDirectory, current, external, old], [current]);
            Assert.AreEqual("new", File.ReadAllText(current));
            Assert.AreEqual("outside", File.ReadAllText(external));
            Assert.IsFalse(File.Exists(old));
        }
        finally
        {
            temp.Delete(true);
        }
    }

    [TestMethod]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public void CleanupDoesNotFollowLinkedDirectories()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Requires Unix symbolic links.");
        var temp = Directory.CreateTempSubdirectory("HistoryImportCleanup-");
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(temp.FullName, "record")).FullName;
            var outside = Directory.CreateDirectory(Path.Combine(temp.FullName, "outside")).FullName;
            var original = Path.Combine(outside, "keep.txt");
            File.WriteAllText(original, "outside");
            var link = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(link, outside);
            HistoryImportAttachmentCleanup.RemoveObsolete(root, [Path.Combine(link, "keep.txt"), link], []);
            Assert.AreEqual("outside", File.ReadAllText(original));
            Assert.IsNotNull(new DirectoryInfo(link).LinkTarget);
        }
        finally
        {
            temp.Delete(true);
        }
    }
}
