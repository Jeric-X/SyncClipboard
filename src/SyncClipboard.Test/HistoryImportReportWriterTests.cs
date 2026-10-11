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
            var result = new HistoryImportResult
            {
                ImportedCount = 1,
                FailureCount = 1
            };
            using var cancellation = new CancellationTokenSource();
            await using (var report = new HistoryImportReportWriter(Path.Combine(directory.FullName, "backup.json"), result))
            {
                Assert.IsTrue(await report.AppendAsync(new(2, "invalid", "invalid metadata"), cancellation.Token));
                Assert.HasCount(1, Directory.GetFiles(directory.FullName));
                cancellation.Cancel();
                await report.CompleteAsync(cancellation.Token);
            }
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
        var writing = HistoryImportReportWriter.WriteLineAsync(stream.Object, "failure detail", cancellation.Token).AsTask();
        await entered.Task;
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => writing);
    }
}
