using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;

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
            await profile.SetTransferData(filePath, verify: true, token);

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

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SyncClipboard-ProfileTransferValidationTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
