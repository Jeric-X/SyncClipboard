using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileTransferValidationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task FilePrepareTransferData_FileChangedAfterVerifiedSetThrows()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "before", token);
            var sourceProfile = new FileProfile(filePath);
            var expectedHash = await sourceProfile.GetHash(token);
            var profile = new FileProfile(null, Path.GetFileName(filePath), expectedHash);
            await profile.SetTransferData(
                filePath,
                TransferDataValidation.Full(),
                token);

            await File.WriteAllTextAsync(filePath, "after", token);

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(testDirectory, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ImagePrepareTransferData_FileChangedAfterHashThrows()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "image.png");
            await File.WriteAllBytesAsync(filePath, [1, 2, 3], token);
            var profile = new ImageProfile(filePath);
            await profile.GetHash(token);

            await File.WriteAllBytesAsync(filePath, [4, 5, 6], token);

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(testDirectory, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TextPrepareTransferData_TransferFileChangedAfterHashThrows()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var profile = new TextProfile(new string('A', 10241));
            await profile.GetHash(token);
            var transferPath = await profile.PrepareTransferData(testDirectory, token);
            Assert.IsNotNull(transferPath);

            await File.WriteAllTextAsync(transferPath, "changed", token);

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(testDirectory, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TextPrepareTransferData_InlineTextDiffersFromStoredHashThrows()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var expectedHash = await new TextProfile("before").GetHash(token);
        var profile = new TextProfile(new ProfilePersistentInfo
        {
            Type = ProfileType.Text,
            Text = "after",
            Size = "after".Length,
            Hash = expectedHash,
        });

        await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
            () => profile.PrepareTransferData(Path.GetTempPath(), token));
    }

    [TestMethod]
    public async Task TextIsLocalDataValid_InlineTextDiffersFromStoredHashReturnsFalse()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var expectedHash = await new TextProfile("before").GetHash(token);
        var profile = new TextProfile(new ProfilePersistentInfo
        {
            Type = ProfileType.Text,
            Text = "after",
            Size = "after".Length,
            Hash = expectedHash,
        });

        Assert.IsFalse(await profile.IsLocalDataValid(false, token));
    }

    [TestMethod]
    public async Task FileTransferDataHash_EqualsFileContentHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "transfer data", token);
            var profile = new FileProfile(filePath);

            await profile.PrepareTransferData(testDirectory, token);

            var contentHash = await Utility.CalculateFileSHA256(filePath, token);
            Assert.AreEqual(contentHash, profile.TransferDataHash);
            Assert.AreNotEqual(contentHash, await profile.GetHash(token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SetTransferData_UnverifiedProfilesAttachFilesWithoutHashing()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "cached.bin");
            var textPath = Path.Combine(testDirectory, "cached.txt");
            var groupPath = Path.Combine(testDirectory, "cached.zip");
            await File.WriteAllBytesAsync(filePath, [1, 2, 3], token);
            await File.WriteAllTextAsync(textPath, "cached text", token);
            await File.WriteAllBytesAsync(groupPath, [4, 5, 6], token);
            Profile[] profiles =
            [
                new FileProfile(null, Path.GetFileName(filePath), new string('A', 64)),
                new TextProfile(new string('T', 10241)),
                new GroupProfile([], new string('B', 64)),
            ];
            string[] paths = [filePath, textPath, groupPath];
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();

            for (var index = 0; index < profiles.Length; index++)
            {
                await profiles[index].SetTransferData(
                    paths[index],
                    TransferDataValidation.Unverified,
                    canceled.Token);

                Assert.IsFalse(profiles[index].HasVerifiedTransferDataHashBinding);
            }
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Persist_ImmediatelyVerifiedTransferDataDoesNotRehash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "verified.bin");
            await File.WriteAllBytesAsync(filePath, [1, 2, 3], token);
            var sourceProfile = new FileProfile(filePath);
            var profile = new FileProfile(
                null,
                Path.GetFileName(filePath),
                await sourceProfile.GetHash(token));
            await profile.SetTransferData(
                filePath,
                TransferDataValidation.Full(),
                token);
            await profile.GetSize(token);
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();

            var persistentInfo = await profile.Persist(testDirectory, canceled.Token);

            Assert.AreEqual(profile.TransferDataHash, persistentInfo.TransferDataHash);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LongTextTransferDataHash_EqualsProfileHashAndPersists()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var profile = new TextProfile(new string('T', 10241));
            var transferPath = await profile.PrepareTransferData(testDirectory, token);
            Assert.IsNotNull(transferPath);

            var persistentInfo = await profile.Persist(testDirectory, token);

            Assert.AreEqual(await profile.GetHash(token), profile.TransferDataHash);
            Assert.AreEqual(profile.TransferDataHash, persistentInfo.TransferDataHash);
            Assert.IsNotNull(persistentInfo.TransferDataFile);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SyncClipboard-ProfileTransferValidationTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
