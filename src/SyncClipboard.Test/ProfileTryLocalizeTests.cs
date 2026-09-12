using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileTryLocalizeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(ProfileType.File)]
    [DataRow(ProfileType.Image)]
    [DataRow(ProfileType.Group)]
    [DataRow(ProfileType.Text)]
    public void SavePath_DoesNotCreateDirectoriesOrChangeTransferHash(ProfileType type)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SyncClipboard-SavePath-{Guid.NewGuid():N}");
        var dto = new ProfileDto
        {
            Type = type,
            Hash = new string('A', 64),
            Text = "display",
            HasData = true,
            DataName = type == ProfileType.Group ? "data.zip" : "data.txt",
            TransferDataHash = new string('B', 64),
        };
        Profile profile = type switch
        {
            ProfileType.File => new FileProfile(dto),
            ProfileType.Image => new ImageProfile(dto),
            ProfileType.Group => new GroupProfile(dto),
            _ => new TextProfile(dto),
        };

        var path = profile.GetTransferDataSavePath(directory);

        Assert.AreEqual(Path.Combine(directory, $"{type}_{dto.Hash}", dto.DataName), path);
        Assert.IsFalse(Directory.Exists(directory));
        Assert.AreEqual(dto.TransferDataHash, profile.TransferDataHash);
        Assert.AreEqual(path, profile.GetTransferDataSavePath(directory));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SavePath_UsesPersistentDirectoryInsteadOfExistingLocalPath(bool image)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-SavePath-");
        try
        {
            var file = Path.Combine(directory.FullName, "file.txt");
            await File.WriteAllTextAsync(file, "content", token);
            FileProfile profile = image ? new ImageProfile(file) : new FileProfile(file);
            var hash = await profile.GetHash(token);
            Assert.IsTrue(await profile.TryLocalize(directory.FullName, false, token));

            var persistentDir = Path.Combine(directory.FullName, "persistent");
            Assert.AreEqual(
                Path.Combine(persistentDir, $"{profile.Type}_{hash}", "file.txt"),
                profile.GetTransferDataSavePath(persistentDir));
            Assert.AreEqual(file, profile.FullPath);
            Assert.AreEqual("content", await File.ReadAllTextAsync(file, token));
            Assert.IsFalse(Directory.Exists(persistentDir));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task Text_TryLocalizeLoadsFullTextFromValidatedFile()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-TryLocalize-");
        try
        {
            var text = new string('T', 20000);
            var source = new TextProfile(text);
            var info = await source.Persist(directory.FullName, token);
            var profile = Profile.Create(directory.FullName, info);

            Assert.IsTrue(await profile.TryLocalize(directory.FullName, false, token));
            var path = Profile.GetFullPath(directory.FullName, info.Type, info.Hash, info.TransferDataFile);
            Assert.IsNotNull(path);
            File.Delete(path);

            Assert.AreEqual(text, (await profile.Localize(directory.FullName, token)).Text);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task Group_MissingDataPreservesPathsWithoutStartingCancellableWork()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");
        var profile = new GroupProfile([path], new string('A', 64));

        Assert.IsFalse(await profile.TryLocalize(Path.GetTempPath(), false, TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(path, profile.Files.Single());
        Assert.AreEqual(Path.GetFileName(path), profile.DisplayText);
        Assert.AreEqual(path, (await profile.Localize(Path.GetTempPath(), TestContext.CancellationTokenSource.Token)).FilePaths.Single());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.IsFalse(await profile.TryLocalize(Path.GetTempPath(), false, cancellation.Token));
        Assert.AreEqual(path, profile.Files.Single());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task File_TryLocalizePropagatesCancellationFromHashCalculation(bool clearInvalidLocalPaths)
    {
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-Cancellation-");
        try
        {
            var path = Path.Combine(directory.FullName, "file.txt");
            await File.WriteAllTextAsync(path, "content", TestContext.CancellationTokenSource.Token);
            var profile = new FileProfile(path, hash: new string('A', 64));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => profile.TryLocalize(directory.FullName, clearInvalidLocalPaths, cancellation.Token));
            Assert.AreEqual(path, profile.FullPath);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Group_ExtractionFailurePropagatesAndPreservesOldPaths(bool clearInvalidLocalPaths)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-TryLocalize-");
        try
        {
            var sourceFile = Path.Combine(directory.FullName, "source.txt");
            await File.WriteAllTextAsync(sourceFile, "source", token);
            var profile = new GroupProfile([sourceFile]);
            var archivePath = (await profile.PrepareTransferData(directory.FullName, token))?.Path;
            Assert.IsNotNull(archivePath);
            await File.WriteAllTextAsync(sourceFile, "modified", token);
            // 解压目标被普通文件占用：这是本地操作失败，不能当成需要下载。
            await File.WriteAllTextAsync(archivePath[..^4], "occupied", token);

            await Assert.ThrowsAsync<IOException>(() => profile.TryLocalize(directory.FullName, clearInvalidLocalPaths, token));

            Assert.AreEqual(sourceFile, profile.Files.Single());
            Assert.AreEqual("modified", await File.ReadAllTextAsync(sourceFile, token));
            Assert.AreEqual("occupied", await File.ReadAllTextAsync(archivePath[..^4], token));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Group_ClearedPathsAllowDownloadedArchiveToReplaceOldFiles(bool missingLocalFile)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-TryLocalize-");
        try
        {
            var sourceFile = Path.Combine(directory.FullName, "source.txt");
            await File.WriteAllTextAsync(sourceFile, "source", token);
            var source = new GroupProfile([sourceFile]);
            var archive = (await source.PrepareTransferData(directory.FullName, token))?.Path;
            Assert.IsNotNull(archive);
            var archiveBytes = await File.ReadAllBytesAsync(archive, token);
            var profile = new GroupProfile(await source.ToProfileDto(token));
            await profile.SetTransferData(archive, verify: true, token);
            var oldPath = profile.Files.Single();
            Assert.IsTrue(await profile.TryLocalize(directory.FullName, true, token));
            Assert.AreEqual(oldPath, profile.Files.Single());
            var displayText = profile.DisplayText;
            var hash = await profile.GetHash(token);
            var size = await profile.GetSize(token);
            var transferHash = profile.TransferDataHash;
            var savePath = profile.GetTransferDataSavePath(directory.FullName);
            if (missingLocalFile)
                File.Delete(oldPath);
            else
                await File.WriteAllTextAsync(oldPath, "modified", token);
            await File.WriteAllTextAsync(archive, "invalid zip", token);

            Assert.IsFalse(await profile.TryLocalize(directory.FullName, true, token));

            Assert.IsEmpty(profile.Files);
            Assert.AreEqual(displayText, profile.DisplayText);
            Assert.AreEqual(hash, await profile.GetHash(token));
            Assert.AreEqual(size, await profile.GetSize(token));
            Assert.AreEqual(transferHash, profile.TransferDataHash);
            Assert.AreEqual(savePath, profile.GetTransferDataSavePath(directory.FullName));
            Assert.AreEqual("invalid zip", await File.ReadAllTextAsync(archive, token));
            if (!missingLocalFile)
                Assert.AreEqual("modified", await File.ReadAllTextAsync(oldPath, token));

            // 模拟官方历史下载：验证新 ZIP 后绑定，再由无验证的 Localize 解压。
            await File.WriteAllBytesAsync(savePath, archiveBytes, token);
            var actualHash = await Utility.VerifyFileSHA256(savePath, transferHash, token);
            await profile.SetTransferData(savePath, actualHash, verify: false, token);
            var localInfo = await profile.Localize(directory.FullName, token);

            Assert.AreEqual("source", await File.ReadAllTextAsync(localInfo.FilePaths.Single(), token));
            Assert.IsTrue(await profile.IsLocalDataValid(false, token));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task Group_CancelledQuickFailureKeepsPathsEvenWhenClearingIsEnabled()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");
        var profile = new GroupProfile([path], new string('A', 64));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.IsFalse(await profile.TryLocalize(Path.GetTempPath(), true, cancellation.Token));
        Assert.AreEqual(path, profile.Files.Single());
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task FileOrImage_ClearsInvalidPathOnlyWhenRequested(bool image, bool clearInvalidLocalPaths)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-TryLocalize-");
        try
        {
            var path = Path.Combine(directory.FullName, "source.txt");
            await File.WriteAllTextAsync(path, "source", token);
            FileProfile profile = image ? new ImageProfile(path) : new FileProfile(path);
            var hash = await profile.GetHash(token);
            var transferHash = profile.TransferDataHash;
            var savePath = profile.GetTransferDataSavePath(directory.FullName);
            Assert.IsTrue(await profile.TryLocalize(directory.FullName, clearInvalidLocalPaths, token));
            Assert.AreEqual(path, profile.FullPath);
            await File.WriteAllTextAsync(path, "modified", token);

            Assert.IsFalse(await profile.TryLocalize(directory.FullName, clearInvalidLocalPaths, token));

            Assert.AreEqual(clearInvalidLocalPaths ? null : path, profile.FullPath);
            Assert.AreEqual("source.txt", profile.FileName);
            Assert.AreEqual(hash, await profile.GetHash(token));
            Assert.AreEqual(transferHash, profile.TransferDataHash);
            Assert.AreEqual(savePath, profile.GetTransferDataSavePath(directory.FullName));
            Assert.AreEqual("modified", await File.ReadAllTextAsync(path, token));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Text_ClearsInvalidPathButPreservesDownloadName(bool clearInvalidLocalPaths)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-TryLocalize-");
        try
        {
            var text = new string('T', 20000);
            var source = new TextProfile(text);
            var info = await source.Persist(directory.FullName, token);
            var profile = Profile.Create(directory.FullName, info);
            var path = Profile.GetFullPath(directory.FullName, info.Type, info.Hash, info.TransferDataFile);
            Assert.IsNotNull(path);
            var savePath = profile.GetTransferDataSavePath(directory.FullName);
            var transferHash = profile.TransferDataHash;
            await File.WriteAllTextAsync(path, "modified", token);

            Assert.IsFalse(await profile.TryLocalize(directory.FullName, clearInvalidLocalPaths, token));

            Assert.AreEqual(!clearInvalidLocalPaths, await profile.IsLocalDataValid(true, token));
            Assert.AreEqual(info.Text, profile.DisplayText);
            Assert.AreEqual(info.Hash, await profile.GetHash(token));
            Assert.AreEqual(transferHash, profile.TransferDataHash);
            Assert.AreEqual(savePath, profile.GetTransferDataSavePath(directory.FullName));
            Assert.AreEqual("modified", await File.ReadAllTextAsync(path, token));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task UnsupportedProfileHasNoSavePath()
    {
        var profile = new UnknownProfile();
        Assert.IsFalse(await profile.TryLocalize(Path.GetTempPath(), false, TestContext.CancellationTokenSource.Token));
        Assert.IsNull(profile.GetTransferDataSavePath(Path.GetTempPath()));
        Assert.IsNull(new TextProfile("inline").GetTransferDataSavePath(Path.GetTempPath()));
    }
}
