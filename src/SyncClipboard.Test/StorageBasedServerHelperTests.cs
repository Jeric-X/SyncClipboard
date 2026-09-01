using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;
using System.Net;

namespace SyncClipboard.Test;

[TestClass]
public class StorageBasedServerHelperTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task DownloadFileProfile_EmptyRemoteHashBackfillsMetadataWithoutUploadingFile()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var fileName = "video.mov";
            var remoteFile = Path.Combine(testDirectory, "remote", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(remoteFile)!);
            await File.WriteAllBytesAsync(remoteFile, [1, 2, 3, 4], token);

            var remoteProfile = new ProfileDto
            {
                Type = ProfileType.File,
                Hash = string.Empty,
                Text = fileName,
                HasData = true,
                DataName = fileName,
                Size = new FileInfo(remoteFile).Length,
            };
            var adapter = new TestStorageAdapter(remoteFile, remoteProfile);
            var helper = CreateHelper(testDirectory, adapter);
            var profile = Profile.Create(remoteProfile);

            await helper.DownloadProfileDataAsync(profile, cancellationToken: token);

            var expectedHash = await new FileProfile(remoteFile).GetHash(token);
            Assert.AreEqual(1, adapter.DownloadCount);
            Assert.AreEqual(0, adapter.UploadCount);
            Assert.AreEqual(1, adapter.SetProfileCount);
            Assert.AreEqual(expectedHash, adapter.CurrentProfile?.Hash);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadFileProfile_RemoteProfileChangedBeforeBackfillDoesNotOverwriteMetadata()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var fileName = "video.mov";
            var remoteFile = Path.Combine(testDirectory, "remote", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(remoteFile)!);
            await File.WriteAllBytesAsync(remoteFile, [1, 2, 3, 4], token);

            var originalProfile = new ProfileDto
            {
                Type = ProfileType.File,
                Hash = string.Empty,
                Text = fileName,
                HasData = true,
                DataName = fileName,
                Size = new FileInfo(remoteFile).Length,
            };
            var newerProfile = new ProfileDto
            {
                Type = ProfileType.Text,
                Hash = "NEW_REMOTE_HASH",
                Text = "new clipboard",
                HasData = false,
                Size = "new clipboard".Length,
            };
            var adapter = new TestStorageAdapter(remoteFile, originalProfile)
            {
                ProfileAfterDownload = newerProfile,
            };
            var helper = CreateHelper(testDirectory, adapter);

            await helper.DownloadProfileDataAsync(
                Profile.Create(originalProfile),
                cancellationToken: token);

            Assert.AreEqual(1, adapter.DownloadCount);
            Assert.AreEqual(0, adapter.SetProfileCount);
            Assert.AreSame(newerProfile, adapter.CurrentProfile);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadFileProfile_ExistingRemoteHashDoesNotRewriteMetadata()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var fileName = "video.mov";
            var remoteFile = Path.Combine(testDirectory, "remote", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(remoteFile)!);
            await File.WriteAllBytesAsync(remoteFile, [1, 2, 3, 4], token);
            var expectedHash = await new FileProfile(remoteFile).GetHash(token);
            var remoteProfile = new ProfileDto
            {
                Type = ProfileType.File,
                Hash = expectedHash,
                Text = fileName,
                HasData = true,
                DataName = fileName,
                Size = new FileInfo(remoteFile).Length,
            };
            var adapter = new TestStorageAdapter(remoteFile, remoteProfile);
            var helper = CreateHelper(testDirectory, adapter);

            await helper.DownloadProfileDataAsync(
                Profile.Create(remoteProfile),
                cancellationToken: token);

            Assert.AreEqual(1, adapter.DownloadCount);
            Assert.AreEqual(0, adapter.SetProfileCount);
            Assert.AreSame(remoteProfile, adapter.CurrentProfile);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static StorageBasedServerHelper CreateHelper(
        string persistentDirectory,
        IStorageBasedServerAdapter adapter)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger, TestLogger>();
        services.AddSingleton<ITrayIcon, TestTrayIcon>();
        services.AddSingleton<IProfileEnv>(new TestProfileEnv(persistentDirectory));
        return new StorageBasedServerHelper(services.BuildServiceProvider(), adapter);
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"SyncClipboard-StorageBasedServerHelperTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestStorageAdapter(
        string remoteFile,
        ProfileDto currentProfile) : IStorageBasedServerAdapter
    {
        public ProfileDto? CurrentProfile { get; private set; } = currentProfile;
        public ProfileDto? ProfileAfterDownload { get; init; }
        public int DownloadCount { get; private set; }
        public int UploadCount { get; private set; }
        public int SetProfileCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ProfileDto?> GetProfileAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CurrentProfile);
        }

        public Task SetProfileAsync(
            ProfileDto profileDto,
            CancellationToken cancellationToken = default)
        {
            SetProfileCount++;
            CurrentProfile = profileDto;
            return Task.CompletedTask;
        }

        public Task UploadFileAsync(
            string fileName,
            string localPath,
            IProgress<HttpDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            UploadCount++;
            return Task.CompletedTask;
        }

        public Task DownloadFileAsync(
            string fileName,
            string localPath,
            IProgress<HttpDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            File.Copy(remoteFile, localPath, overwrite: true);
            CurrentProfile = ProfileAfterDownload ?? CurrentProfile;
            return Task.CompletedTask;
        }

        public Task CleanupTempFilesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TestConnectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void ApplyConfig() { }
        public void SetConfig(object config, SyncConfig syncConfig) { }
        public void SetProxy(IWebProxy proxy) { }
    }

    private sealed class TestProfileEnv(string persistentDirectory) : IProfileEnv
    {
        public string GetPersistentDir() => persistentDirectory;
        public string GetHistoryPersistentDir() => persistentDirectory;
    }

    private sealed class TestLogger : ILogger
    {
        public void Write(string? tag, string str) { }
        public void Write(string str) { }
        public Task WriteAsync(string? tag, string str) => Task.CompletedTask;
        public Task WriteAsync(string str) => Task.CompletedTask;
        public void Flush() { }
    }

    private sealed class TestTrayIcon : ITrayIcon
    {
        public event Action? LeftClicked { add { } remove { } }
        public event Action? DoubleClicked { add { } remove { } }

        public void Create() { }
        public void ShowUploadAnimation() { }
        public void ShowDownloadAnimation() { }
        public void StopAnimation() { }
        public void SetStatusString(string key, string statusStr, bool error) { }
        public void SetStatusString(string key, string statusStr) { }
        public void SetActiveStatus(bool active) { }
    }
}
