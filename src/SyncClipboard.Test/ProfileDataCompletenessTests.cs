using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileDataCompletenessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("valid", "missing", true, true)]
    [DataRow("valid", "modified", true, true)]
    [DataRow("missing", "valid", true, true)]
    [DataRow("modified", "valid", true, true)]
    [DataRow("missing", "valid", false, true)]
    [DataRow("missing", "modified", true, false)]
    [DataRow("missing", "modified", false, false)]
    [DataRow("missing", "missing", true, false)]
    public async Task Group_AnyCompleteRepresentationIsEnough(
        string sourceState,
        string archiveState,
        bool hasTransferHash,
        bool expectedComplete)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var sourceFile = Path.Combine(testDirectory, "source.txt");
            await File.WriteAllTextAsync(sourceFile, "source", token);
            var sourceProfile = new GroupProfile([sourceFile]);
            var profileHash = await sourceProfile.GetHash(token);
            var archivePath = (await sourceProfile.PrepareTransferData(testDirectory, token))?.Path;
            Assert.IsNotNull(archivePath);
            var transferHash = sourceProfile.TransferDataHash;

            if (sourceState == "missing")
                File.Delete(sourceFile);
            else if (sourceState == "modified")
                await File.WriteAllTextAsync(sourceFile, "modified", token);

            if (archiveState == "missing")
                File.Delete(archivePath);
            else if (archiveState == "modified")
                await File.WriteAllTextAsync(archivePath, "invalid archive", token);

            Profile profile = new GroupProfile(
                [sourceFile], profileHash, archivePath, hasTransferHash ? transferHash : null);

            Assert.AreEqual(sourceState != "missing" || archiveState != "missing", await profile.IsDataComplete(true, token));
            Assert.AreEqual(hasTransferHash ? transferHash : null, profile.TransferDataHash);
            Assert.IsFalse(Directory.Exists(archivePath[..^4]));
            Assert.AreEqual(expectedComplete, await profile.IsDataComplete(false, token));
            Assert.IsFalse(Directory.Exists(archivePath[..^4]));
            Assert.AreEqual(sourceState != "missing", File.Exists(sourceFile));
            if (sourceState == "modified")
                Assert.AreEqual("modified", await File.ReadAllTextAsync(sourceFile, token));
            if (sourceState == "missing" && archiveState == "valid")
                Assert.AreEqual(transferHash, profile.TransferDataHash);

            Assert.AreEqual(expectedComplete, await profile.TryLocalize(testDirectory, false, token));
            if (expectedComplete)
                Assert.IsTrue(await profile.IsLocalDataValid(false, token));
            else
                CollectionAssert.AreEqual(new[] { sourceFile }, ((GroupProfile)profile).Files);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FileOrImage_RejectsChangedAndMissingFile(bool image)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.png");
            await File.WriteAllBytesAsync(filePath, [1, 2, 3], token);
            Profile profile = image ? new ImageProfile(filePath) : new FileProfile(filePath);
            await profile.GetHash(token);

            Assert.IsTrue(await profile.IsDataComplete(true, token));
            Assert.IsTrue(await profile.IsDataComplete(false, token));
            await File.WriteAllBytesAsync(filePath, [4, 5, 6], token);
            Assert.IsTrue(await profile.IsDataComplete(true, token));
            Assert.IsFalse(await profile.IsDataComplete(false, token));
            File.Delete(filePath);
            Assert.IsFalse(await profile.IsDataComplete(true, token));
            Assert.IsFalse(await profile.IsDataComplete(false, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(5)]
    [DataRow(10241)]
    public async Task Text_CompleteInMemoryDoesNotRequireTransferFile(int length)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            Profile profile = new TextProfile(new string('T', length));

            Assert.IsTrue(await profile.IsDataComplete(true, token));
            Assert.IsTrue(await profile.IsDataComplete(false, token));
            Assert.IsTrue(await profile.TryLocalize(testDirectory, false, token));
            Assert.IsFalse(Directory.EnumerateFileSystemEntries(testDirectory).Any());
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Text_QuickCheckOnlyRequiresTransferFileToExist(bool hasTransferHash)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var sourceProfile = new TextProfile(new string('T', 10241));
            var transferPath = (await sourceProfile.PrepareTransferData(testDirectory, token))?.Path;
            Assert.IsNotNull(transferPath);
            var transferHash = hasTransferHash ? sourceProfile.TransferDataHash : null;
            Profile profile = new TextProfile(new ProfilePersistentInfo
            {
                Type = ProfileType.Text,
                Text = sourceProfile.ShortDisplayText,
                Size = await sourceProfile.GetSize(token),
                Hash = await sourceProfile.GetHash(token),
                TransferDataFile = transferPath,
                TransferDataHash = transferHash,
            });

            Assert.IsTrue(await profile.IsDataComplete(true, token));
            await File.WriteAllTextAsync(transferPath, "modified", token);
            Assert.IsTrue(await profile.IsDataComplete(true, token));
            Assert.AreEqual(transferHash, profile.TransferDataHash);
            Assert.IsFalse(await profile.IsDataComplete(false, token));
            File.Delete(transferPath);
            Assert.IsFalse(await profile.IsDataComplete(true, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Unknown_HasNoCompleteData()
    {
        Profile profile = new UnknownProfile();
        Assert.IsFalse(await profile.IsDataComplete(true, TestContext.CancellationTokenSource.Token));
        Assert.IsFalse(await profile.IsDataComplete(false, TestContext.CancellationTokenSource.Token));
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SyncClipboard-DataCompleteness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
