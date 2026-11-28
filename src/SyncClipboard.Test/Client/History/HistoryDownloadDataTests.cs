using System.Text;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryDownloadDataTests
{
    public TestContext TestContext { get; set; } = null!;
    private string _root = null!;

    [TestInitialize]
    public void Initialize() => _root = Directory.CreateTempSubdirectory().FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void RemoteHistoryMetadata_DoesNotCreateLocalTransferDataHashBinding()
    {
        var record = new HistoryRecordDto { Type = ProfileType.Group, HasData = true, Hash = new string('E', 64) }.ToHistoryRecord();

        Assert.IsNull(record.TransferDataFile);
        Assert.IsNull(record.TransferDataHash);
        Assert.IsFalse(record.IsLocalFileReady);
    }

    [TestMethod]
    public async Task SaveHistoryDataResponse_MatchingHeaderPublishesFileAndReturnsHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var bytes = Encoding.UTF8.GetBytes("downloaded history data");
        var transferDataHash = await Utility.CalculateSHA256(bytes, token);
        using var response = CreateTransferDataResponse(bytes, transferDataHash);
        var localPath = Path.Combine(_root, "download", "data.bin");

        var actualHash = await OfficialAdapter.SaveHistoryDataResponseAsync(response, localPath, progress: null, token);

        Assert.AreEqual(transferDataHash, actualHash);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(localPath, token));
    }

    [TestMethod]
    public async Task SaveHistoryDataResponse_MismatchedHeaderDoesNotReplaceExistingFile()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var localPath = Path.Combine(_root, "data.bin");
        await File.WriteAllTextAsync(localPath, "existing", token);
        using var response = CreateTransferDataResponse(Encoding.UTF8.GetBytes("rejected"), new string('A', 64));

        await Assert.ThrowsExactlyAsync<RemoteHistoryDataRejectedException>(
            () => OfficialAdapter.SaveHistoryDataResponseAsync(response, localPath, progress: null, token));

        Assert.AreEqual("existing", await File.ReadAllTextAsync(localPath, token));
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.download").Length);
    }

    private static HttpResponseMessage CreateTransferDataResponse(byte[] bytes, string transferDataHash)
    {
        var response = new HttpResponseMessage
        {
            Content = new ByteArrayContent(bytes),
        };
        response.Headers.Add(HistoryTransferDataHeaders.TransferDataHash, transferDataHash);
        return response;
    }
}
