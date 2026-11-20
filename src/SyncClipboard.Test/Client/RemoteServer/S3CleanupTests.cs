using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter.S3Server;
using System.Reflection;

namespace SyncClipboard.Test.Client.RemoteServer;

[TestClass]
public class S3CleanupTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task CleanupTempFilesAsync_DeletesEveryPageWhenOnlySingleDeletesAreSupported()
    {
        const string bucket = "clipboard";
        const string prefix = "syncclipboard/file/";
        var token = TestContext.CancellationTokenSource.Token;
        var client = new Mock<AmazonS3Client>(new AnonymousAWSCredentials(), RegionEndpoint.USEast1);
        using var adapter = new S3Adapter(Mock.Of<ILogger>());
        var config = new S3Config
        {
            BucketName = bucket,
            AccessKeyId = "key",
            SecretAccessKey = "secret",
            DeleteFilesIndividually = true
        };
        adapter.SetConfig(config, new SyncConfig());
        var field = typeof(S3Adapter).GetField("_s3Client", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((AmazonS3Client)field.GetValue(adapter)!).Dispose();
        field.SetValue(adapter, client.Object);

        client.Setup(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r =>
            r.BucketName == bucket && r.Prefix == prefix && r.ContinuationToken == null), token))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = [new() { Key = prefix + "a" }, new() { Key = prefix + "b" }],
                IsTruncated = true,
                NextContinuationToken = "next"
            });
        client.Setup(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r =>
            r.BucketName == bucket && r.Prefix == prefix && r.ContinuationToken == "next"), token))
            .ReturnsAsync(new ListObjectsV2Response { S3Objects = [new() { Key = prefix + "c" }] });
        client.Setup(x => x.DeleteObjectsAsync(It.IsAny<DeleteObjectsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("Missing Some Required Arguments."));
        var deletedKeys = new List<string>();
        client.Setup(x => x.DeleteObjectAsync(bucket, It.IsAny<string>(), token))
            .Callback<string, string, CancellationToken>((_, key, _) => deletedKeys.Add(key))
            .ReturnsAsync(new DeleteObjectResponse());

        await adapter.CleanupTempFilesAsync(token);

        CollectionAssert.AreEquivalent(new[] { prefix + "a", prefix + "b", prefix + "c" }, deletedKeys);
        client.Verify(x => x.DeleteObjectsAsync(It.IsAny<DeleteObjectsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
