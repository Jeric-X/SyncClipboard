using SyncClipboard.Shared;
using SyncClipboard.Shared.Models;
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
            await profile.SetTransferData(filePath, verify: true, token);
            var expectedTransferDataHash = profile.TransferDataHash;

            await File.WriteAllTextAsync(filePath, "after", token);

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(testDirectory, token));

            Assert.AreEqual(expectedHash, await profile.GetHash(token));
            Assert.AreEqual(expectedTransferDataHash, profile.TransferDataHash);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FilePrepareTransferData_WrapsReadFailureButPropagatesCancellation()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "content", token);
            var profile = new FileProfile(filePath);
            var expectedHash = await profile.GetHash(token);
            var expectedTransferHash = profile.TransferDataHash;

            await using (var lockedFile = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var exception = await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                    () => profile.PrepareTransferData(testDirectory, token));
                Assert.IsInstanceOfType<IOException>(exception.InnerException);
            }

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => profile.PrepareTransferData(testDirectory, cancellation.Token));

            Assert.AreEqual(expectedHash, await profile.GetHash(token));
            Assert.AreEqual(expectedTransferHash, profile.TransferDataHash);
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
            var expectedHash = await profile.GetHash(token);
            var expectedTransferDataHash = profile.TransferDataHash;

            await File.WriteAllBytesAsync(filePath, [4, 5, 6], token);

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(testDirectory, token));

            Assert.AreEqual(expectedHash, await profile.GetHash(token));
            Assert.AreEqual(expectedTransferDataHash, profile.TransferDataHash);
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
            var transferPath = (await profile.PrepareTransferData(testDirectory, token))?.Path;
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
    [DataRow("matching", true)]
    [DataRow("missing", true)]
    [DataRow("mismatched", false)]
    public async Task TextIsLocalDataValid_ValidatesRemoteInlineText(string hashKind, bool expectedValid)
    {
        var token = TestContext.CancellationTokenSource.Token;
        const string text = "remote inline text";
        var actualHash = await Utility.CalculateSHA256(text, token);
        var declaredHash = hashKind switch
        {
            "matching" => actualHash.ToLowerInvariant(),
            "missing" => null,
            _ => new string('A', 64)
        };
        var profile = Profile.Create(new ProfileDto
        {
            Type = ProfileType.Text,
            Text = text,
            Hash = declaredHash!,
            HasData = false,
        });

        Assert.AreEqual(expectedValid, await profile.IsLocalDataValid(false, token));
        Assert.IsNull(profile.TransferDataHash);
        if (expectedValid)
        {
            Assert.IsTrue(Utility.SHA256Same(actualHash, await profile.GetHash(token)));
        }
        else
        {
            Assert.AreEqual(declaredHash, await profile.GetHash(token));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TextIsLocalDataValid_FallsBackToFileWhenInMemoryHashDoesNotMatch(bool validFile)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "text.txt");
            var expectedText = new string('F', 10241);
            await File.WriteAllTextAsync(filePath, expectedText, token);
            var expectedHash = await Utility.CalculateFileSHA256(filePath, token);
            var profile = new TextProfile(new string('T', 10241));
            await profile.SetTransferData(new FileHashInfo(filePath, expectedHash), false, token);
            if (!validFile)
                await File.WriteAllTextAsync(filePath, "changed", token);

            Assert.AreEqual(validFile, await profile.IsLocalDataValid(false, token));
            Assert.AreEqual(expectedHash, await profile.GetHash(token));
            Assert.AreEqual(expectedHash, profile.TransferDataHash);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
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
                await profiles[index].SetTransferData(paths[index], verify: false, canceled.Token);

                Assert.IsNull(profiles[index].TransferDataHash);
                Assert.IsFalse(await Utility.FileMatchesSHA256(paths[index], profiles[index].TransferDataHash, token));
            }
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(ProfileType.File)]
    [DataRow(ProfileType.Image)]
    [DataRow(ProfileType.Text)]
    public async Task Persist_DoesNotValidateTransferData(ProfileType type)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "data.txt");
            await File.WriteAllTextAsync(filePath, "actual content", token);
            var declaredHash = new string('A', 64);
            var profile = Profile.Create(new ProfileDto
            {
                Type = type,
                Hash = declaredHash,
                Text = "preview",
                HasData = true,
                DataName = Path.GetFileName(filePath),
            });
            await profile.SetTransferData(filePath, false, token);

            var persistentInfo = await profile.Persist(testDirectory, token);

            Assert.AreEqual(declaredHash, persistentInfo.Hash);
            Assert.IsNotNull(persistentInfo.TransferDataFile);
            Assert.IsNull(persistentInfo.TransferDataHash);

            var expectedTransferHash = await Utility.CalculateFileSHA256(filePath, token);
            await profile.SetTransferData(new FileHashInfo(filePath, expectedTransferHash), false, token);

            var boundInfo = await profile.Persist(testDirectory, token);

            Assert.AreEqual(declaredHash, boundInfo.Hash);
            Assert.AreEqual(persistentInfo.TransferDataFile, boundInfo.TransferDataFile);
            Assert.AreEqual(expectedTransferHash, boundInfo.TransferDataHash);
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
            var transferPath = (await profile.PrepareTransferData(testDirectory, token))?.Path;
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
