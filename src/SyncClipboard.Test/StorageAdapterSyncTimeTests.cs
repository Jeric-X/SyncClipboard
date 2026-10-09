using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.RemoteServer.Adapter.S3Server;
using SyncClipboard.Core.RemoteServer.Adapter.WebDavServer;
using SyncClipboard.Core.Utilities.Web;
using SyncClipboard.Shared;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
public class StorageAdapterSyncTimeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, false)]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    public async Task WriteProfile_FillsMissingSyncTimeAndPreservesExistingOffset(
        bool s3, bool conditional, bool suppliedTime)
    {
        string? uploadedJson = null;
        IServerAdapter adapter = s3
            ? CreateS3Adapter(json => uploadedJson = json)
            : CreateWebDavAdapter(json => uploadedJson = json);
        using var disposable = (IDisposable)adapter;
        DateTimeOffset? originalTime = suppliedTime
            ? new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(-7))
            : null;
        var dto = new ProfileDto { Text = "clipboard", SyncedAt = originalTime };
        var token = TestContext.CancellationTokenSource.Token;
        var before = DateTimeOffset.Now;

        if (conditional)
        {
            Assert.IsTrue(await ((IStorageBasedServerAdapter)adapter).TrySetProfileAsync(dto, "\"version-1\"", token));
        }
        else
        {
            await adapter.SetProfileAsync(dto, token);
        }

        var after = DateTimeOffset.Now;
        Assert.IsNotNull(uploadedJson);
        var uploaded = JsonSerializer.Deserialize<ProfileDto>(uploadedJson, JsonSerializerOptions.Web);
        Assert.IsNotNull(uploaded?.SyncedAt);
        var syncedAt = uploaded.SyncedAt.Value;
        if (originalTime.HasValue)
        {
            Assert.IsTrue(originalTime.Value.EqualsExact(syncedAt));
        }
        else
        {
            Assert.IsTrue(syncedAt >= before && syncedAt <= after);
            Assert.AreEqual(TimeZoneInfo.Local.GetUtcOffset(syncedAt), syncedAt.Offset);
        }
    }

    private static WebDavAdapter CreateWebDavAdapter(Action<string> recordJson)
    {
        var adapter = new WebDavAdapter(Mock.Of<ILogger>(), Mock.Of<IAppConfig>());
        var webDav = typeof(WebDavAdapter).GetField("_webDav", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(adapter)!;
        var client = new HttpClient(new RecordingHandler(recordJson)) { BaseAddress = new Uri("https://storage.test/") };
        typeof(WebDavBase).GetField("httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(webDav, client);
        return adapter;
    }

    private static S3Adapter CreateS3Adapter(Action<string> recordJson)
    {
        var adapter = new S3Adapter(Mock.Of<ILogger>());
        adapter.SetConfig(new S3Config
        {
            BucketName = "clipboard",
            AccessKeyId = "test-key",
            SecretAccessKey = "test-secret",
        }, new SyncConfig());
        var client = new Mock<AmazonS3Client>(new AnonymousAWSCredentials(), RegionEndpoint.USEast1);
        client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => recordJson(request.ContentBody))
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });
        var field = typeof(S3Adapter).GetField("_s3Client", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((AmazonS3Client)field.GetValue(adapter)!).Dispose();
        field.SetValue(adapter, client.Object);
        return adapter;
    }

    private sealed class RecordingHandler(Action<string> recordJson) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            recordJson(await request.Content!.ReadAsStringAsync(token));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
