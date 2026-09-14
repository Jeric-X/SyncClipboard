using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
[TestCategory("NonUI")]
public class FileSysCancellationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RecursiveCleanup_PreservesRemainingEntriesWhenCanceled(bool cancelBeforeStart)
    {
        var root = Path.Combine(Path.GetTempPath(), "syncclipboard-cleanup-" + Guid.NewGuid());
        var first = Directory.CreateDirectory(Path.Combine(root, "first"));
        var child = Directory.CreateDirectory(Path.Combine(first.FullName, "nested"));
        File.WriteAllText(Path.Combine(child.FullName, "data.txt"), "temporary fixture");
        var second = new FileInfo(Path.Combine(root, "remaining.txt"));
        File.WriteAllText(second.FullName, "must remain after cancellation");
        using var cancellation = new CancellationTokenSource();
        IEnumerable<FileSystemInfo> Entries()
        {
            yield return first;
            // The first recursive deletion has completed; cancel before the next destructive operation.
            Assert.IsFalse(Directory.Exists(first.FullName));
            cancellation.Cancel();
            yield return second;
        }

        try
        {
            if (cancelBeforeStart) cancellation.Cancel();
            Assert.ThrowsExactly<OperationCanceledException>(() => FileSys.DeleteFileSystemEntries(Entries(), cancellation.Token));
            Assert.AreEqual(cancelBeforeStart, Directory.Exists(first.FullName));
            Assert.IsTrue(File.Exists(second.FullName));
            FileSys.DeleteFileSystemEntries([new DirectoryInfo(root)], CancellationToken.None);
            Assert.IsFalse(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
