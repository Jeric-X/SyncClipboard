using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter.S3Server;
using SyncClipboard.Shared;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

try
{
    await RunAsync(args);
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}

static async Task RunAsync(string[] args)
{
    if (args.Length != 3)
    {
        throw new ArgumentException("Expected endpoint, fault proxy and isolated output directory.");
    }
    var endpoint = new Uri(args[0]);
    var proxyEndpoint = new Uri(args[1]);
    Require(endpoint.Scheme == "http" && endpoint.Host == "127.0.0.1", "Use the isolated loopback server.");
    Require(proxyEndpoint.Scheme == "http" && proxyEndpoint.Host == "127.0.0.1", "Use the isolated loopback proxy.");
    var root = Directory.CreateDirectory(args[2]).FullName;
    var accessKey = Environment.GetEnvironmentVariable("SYNCCLIPBOARD_S3_TEST_ACCESS_KEY")
        ?? throw new InvalidOperationException("Missing test access key.");
    var secretKey = Environment.GetEnvironmentVariable("SYNCCLIPBOARD_S3_TEST_SECRET_KEY")
        ?? throw new InvalidOperationException("Missing test secret key.");
    var bucket = "syncclipboard-upgrade-" + Guid.NewGuid().ToString("N");
    var config = new S3Config
    {
        ServiceURL = endpoint.ToString(),
        Region = "us-east-1",
        BucketName = bucket,
        ObjectPrefix = "/upgrade\\中文 prefix/",
        ForcePathStyle = true,
        AccessKeyId = accessKey,
        SecretAccessKey = secretKey
    };
    const string prefix = "upgrade/中文 prefix";
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    var token = deadline.Token;
    using var client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), new AmazonS3Config
    {
        ServiceURL = endpoint.ToString(),
        AuthenticationRegion = "us-east-1",
        ForcePathStyle = true,
        UseHttp = true,
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        HttpClientFactory = new DirectHttpClientFactory()
    });
    var checks = new List<string>();
    var persistentFailureCanceled = false;
    await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, token);
    try
    {
        using var adapter = CreateAdapter(config);
        await adapter.TestConnectionAsync(token);
        Require(await adapter.GetProfileAsync(token) is null, "Missing profile must return null.");
        await adapter.CleanupTempFilesAsync(token);
        await adapter.InitializeAsync(token);
        await adapter.InitializeAsync(token);
        using (var marker = await client.GetObjectAsync(bucket, prefix + "/file/", token))
        {
            Require(marker.ContentLength == 0, "Directory marker must be empty.");
        }
        Passed("connection, empty list, missing profile and idempotent initialization");

        var profile = new ProfileDto { Text = "升级测试\n中文 😀", Hash = Hash(Encoding.UTF8.GetBytes("升级测试\n中文 😀")) };
        await adapter.SetProfileAsync(profile, token);
        var snapshot = await adapter.GetProfileSnapshotAsync(token);
        Require(snapshot?.Profile == profile && !string.IsNullOrEmpty(snapshot.Version), "Profile or ETag differs.");
        var updated = profile with { Text = "conditional update", Hash = Hash(Encoding.UTF8.GetBytes("conditional update")) };
        Require(await adapter.TrySetProfileAsync(updated, snapshot!.Version, token), "Matching ETag update failed.");
        Require(!await adapter.TrySetProfileAsync(profile, snapshot.Version, token), "Stale ETag overwrote a profile.");
        Require(!await adapter.TrySetProfileAsync(profile, null, token), "Missing ETag was accepted.");
        Require(await adapter.GetProfileAsync(token) == updated, "Conditional write changed the wrong profile.");
        adapter.ApplyConfig();
        Require(await adapter.GetProfileAsync(token) == updated, "Recreating the client lost persisted data.");
        Passed("Unicode metadata, ETags, conditional conflict and client reconfiguration");

        foreach (var size in new[] { 0, 1024, 2 * 1024 * 1024, 32 * 1024 * 1024 })
        {
            var bytes = new byte[size];
            new Random(size + 42).NextBytes(bytes);
            await RoundTrip(adapter, "文件 + % " + size + ".bin", bytes);
        }
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            RoundTrip(adapter, "multiple-" + i + ".bin", Encoding.UTF8.GetBytes("多文件 " + i))));
        var transferBytes = Encoding.UTF8.GetBytes("transfer data\n中文");
        await RoundTrip(adapter, "transfer.bin", transferBytes);
        var transferProfile = profile with
        {
            HasData = true,
            DataName = "transfer.bin",
            Size = transferBytes.Length,
            TransferDataHash = Hash(transferBytes)
        };
        await adapter.SetProfileAsync(transferProfile, token);
        Require(await adapter.GetProfileAsync(token) == transferProfile, "Transfer hash metadata changed.");
        Passed("empty, binary, Unicode keys, 32 MiB, multiple files, transfer hashes and completion progress");

        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await ExpectCanceled(() => adapter.TestConnectionAsync(canceled.Token));
        }
        var cancelBytes = new byte[8 * 1024 * 1024];
        new Random(71).NextBytes(cancelBytes);
        var cancelSource = Path.Combine(root, "cancel-source.bin");
        await File.WriteAllBytesAsync(cancelSource, cancelBytes, token);
        const string preservedObject = "previous complete object";
        await Put(prefix + "/file/cancel-upload.bin", preservedObject);
        using (var cancelUpload = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            var progress = new InlineProgress(p => { if (p.BytesReceived >= 128 * 1024) cancelUpload.Cancel(); });
            await ExpectCanceled(() => adapter.UploadFileAsync("cancel-upload.bin", cancelSource, progress, cancelUpload.Token));
        }
        using (var preserved = await client.GetObjectAsync(bucket, prefix + "/file/cancel-upload.bin", token))
        using (var reader = new StreamReader(preserved.ResponseStream))
        {
            Require(await reader.ReadToEndAsync(token) == preservedObject, "Canceled upload damaged the previous object.");
        }
        await adapter.UploadFileAsync("cancel-download.bin", cancelSource, cancellationToken: token);
        using (var cancelDownload = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            var progress = new InlineProgress(p => { if (p.BytesReceived >= 128 * 1024) cancelDownload.Cancel(); });
            await ExpectCanceled(() => adapter.DownloadFileAsync("cancel-download.bin", Path.Combine(root, "canceled.bin"), progress, cancelDownload.Token));
        }
        await RoundTrip(adapter, "cancel-upload.bin", cancelBytes);
        Passed("pre-cancellation, upload source-read cancellation, in-flight download cancellation and subsequent transfer");

        using (var retried = CreateAdapter(config, new WebProxy(proxyEndpoint)))
        {
            await RoundTrip(retried, "retry-upload.bin", cancelBytes);
            await retried.DownloadFileAsync("retry-download.bin", Path.Combine(root, "retry-download.bin"), cancellationToken: token);
            Require(Hash(await File.ReadAllBytesAsync(Path.Combine(root, "retry-download.bin"), token)) == Hash(cancelBytes), "Retried download differs.");
            using var retryDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            retryDeadline.CancelAfter(TimeSpan.FromSeconds(20));
            var retryWatch = Stopwatch.StartNew();
            try
            {
                await retried.UploadFileAsync("always-fail.bin", cancelSource, cancellationToken: retryDeadline.Token);
                throw new InvalidOperationException("Persistent server failure was ignored.");
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.InternalServerError) { }
            catch (OperationCanceledException) when (retryDeadline.IsCancellationRequested && !token.IsCancellationRequested)
            {
                persistentFailureCanceled = true;
            }
            Require(retryWatch.Elapsed < TimeSpan.FromSeconds(25), "Persistent failure did not terminate within the cancellation bound.");
        }
        Passed("real server through fault proxy: bounded retries and unchanged uploaded/downloaded bytes");

        using (var wrong = CreateAdapter(config with { SecretAccessKey = secretKey + "wrong" }))
        {
            try
            {
                await wrong.TestConnectionAsync(token);
                throw new InvalidOperationException("Invalid credentials were accepted.");
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Forbidden) { }
        }
        using (var alternate = CreateAdapter(config with { ForcePathStyle = false }))
        {
            await alternate.TestConnectionAsync(token);
            Require(await alternate.GetProfileAsync(token) == transferProfile, "Alternate addressing lost data.");
        }
        Passed("invalid credentials rejected; configured addressing and prefix retain data");

        await Put("outside/sentinel", "keep");
        await Parallel.ForEachAsync(Enumerable.Range(0, 1005), new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = token },
            async (i, _) => await Put(prefix + "/file/page-" + i.ToString("D4"), ""));
        using (var keep = CreateAdapter(config with { DeletePreviousFilesOnPush = false }))
        {
            await keep.CleanupTempFilesAsync(token);
            var before = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, Prefix = prefix + "/file/", MaxKeys = 1000 }, token);
            Require(before.IsTruncated == true, "Pagination fixture or keep-files setting failed.");
        }
        await adapter.CleanupTempFilesAsync(token);
        await adapter.CleanupTempFilesAsync(token);
        var after = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, Prefix = prefix + "/file/" }, token);
        Require((after.S3Objects?.Count ?? 0) == 0, "Paginated cleanup left files behind.");
        Require(await adapter.GetProfileAsync(token) == transferProfile, "Cleanup removed metadata.");
        using var sentinel = await client.GetObjectAsync(bucket, "outside/sentinel", token);
        using var sentinelReader = new StreamReader(sentinel.ResponseStream);
        Require(await sentinelReader.ReadToEndAsync(token) == "keep", "Cleanup escaped its prefix.");
        Passed("1005+ object pagination, keep-files setting, repeated empty cleanup and prefix isolation");

        await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
        {
            s3Assembly = typeof(AmazonS3Client).Assembly.GetName().Version?.ToString(),
            s3Sha256 = Hash(await File.ReadAllBytesAsync(typeof(AmazonS3Client).Assembly.Location, token)),
            maximumAttempts = client.Config.MaxErrorRetry + 1,
            persistentFailureCanceled,
            checks
        }, new JsonSerializerOptions { WriteIndented = true }), token);
    }
    finally
    {
        using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (true)
        {
            var remaining = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1000 }, cleanupDeadline.Token);
            if (remaining.S3Objects is null || remaining.S3Objects.Count == 0) break;
            var delete = new DeleteObjectsRequest { BucketName = bucket };
            foreach (var item in remaining.S3Objects) delete.AddKey(item.Key);
            await client.DeleteObjectsAsync(delete, cleanupDeadline.Token);
        }
        await client.DeleteBucketAsync(bucket, cleanupDeadline.Token);
    }

    S3Adapter CreateAdapter(S3Config settings, IWebProxy? proxy = null)
    {
        var adapter = new S3Adapter(new QuietLogger());
        adapter.SetConfig(settings, new SyncConfig { TimeOut = 15 });
        adapter.SetProxy(proxy ?? new WebProxy());
        adapter.ApplyConfig();
        return adapter;
    }

    async Task Put(string key, string text)
    {
        await client.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, ContentBody = text }, token);
    }

    async Task RoundTrip(S3Adapter adapter, string name, byte[] bytes)
    {
        var id = Guid.NewGuid().ToString("N");
        var source = Path.Combine(root, id + ".source");
        var destination = Path.Combine(root, id + ".download");
        await File.WriteAllBytesAsync(source, bytes, token);
        HttpDownloadProgress lastUpload = default, lastDownload = default;
        ulong maximumUpload = 0;
        await adapter.UploadFileAsync(name, source, new InlineProgress(p =>
        {
            lastUpload = p;
            maximumUpload = Math.Max(maximumUpload, p.BytesReceived);
        }), token);
        await adapter.DownloadFileAsync(name, destination, new InlineProgress(p => lastDownload = p), token);
        Require(Hash(bytes) == Hash(await File.ReadAllBytesAsync(destination, token)), "File bytes differ: " + name);
        Require(lastUpload.End && lastUpload.BytesReceived == (ulong)bytes.Length, "Upload completion progress differs.");
        Require(maximumUpload <= (ulong)bytes.Length, "Upload progress exceeds file length.");
        Require(lastDownload.End && lastDownload.BytesReceived == (ulong)bytes.Length, "Download completion progress differs.");
        if (name == "retry-upload.bin")
        {
            await adapter.UploadFileAsync("retry-download.bin", source, cancellationToken: token);
        }
    }

    void Passed(string message)
    {
        checks.Add(message);
        Console.WriteLine("S3 smoke pass: " + message);
    }

    static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static async Task ExpectCanceled(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Cancellation was ignored.");
    }
}

sealed class InlineProgress(Action<HttpDownloadProgress> report) : IProgress<HttpDownloadProgress>
{
    public void Report(HttpDownloadProgress value) => report(value);
}

sealed class DirectHttpClientFactory : Amazon.Runtime.HttpClientFactory
{
    public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(new HttpClientHandler { UseProxy = false });
}

sealed class QuietLogger : ILogger
{
    public void Write(string? tag, string str) { }
    public void Write(string str) { }
    public Task WriteAsync(string? tag, string str) => Task.CompletedTask;
    public Task WriteAsync(string str) => Task.CompletedTask;
    public void Flush() { }
}
