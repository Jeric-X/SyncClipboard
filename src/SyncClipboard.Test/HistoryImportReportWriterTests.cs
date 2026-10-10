using Moq;
using SyncClipboard.Core.Utilities.History.HistoryImport;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryImportReportWriterTests
{
    [TestMethod]
    public async Task CanceledReport_IsNotReportedAsWriteFailureAndRemovesIncompleteFile()
    {
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-ImportReport-");
        try
        {
            var result = new HistoryImportResult { ImportedCount = 1 };
            result.Failures.Add(new(2, "invalid", "invalid metadata"));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await HistoryImportReportWriter.WriteAsync(Path.Combine(directory.FullName, "backup.json"), result, cancellation.Token);
            Assert.IsTrue(result.Canceled);
            Assert.IsNull(result.ReportError);
            Assert.IsNull(result.ReportPath);
            Assert.AreEqual(1, result.ImportedCount);
            Assert.IsEmpty(Directory.GetFiles(directory.FullName));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task ReportWrite_PropagatesCancellationToPendingIo()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new Mock<Stream>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stream.Setup(s => s.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns((ReadOnlyMemory<byte> bytes, CancellationToken token) =>
            {
                entered.SetResult();
                return new ValueTask(Task.Delay(Timeout.Infinite, token));
            });
        var writing = HistoryImportReportWriter.WriteContentsAsync(stream.Object, "backup.json", new(), cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => writing);
    }
}
