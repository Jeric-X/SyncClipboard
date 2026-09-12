using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileTransferDataReplacementTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(ProfileType.File, false, false)]
    [DataRow(ProfileType.File, true, false)]
    [DataRow(ProfileType.File, false, true)]
    [DataRow(ProfileType.File, true, true)]
    [DataRow(ProfileType.Image, false, false)]
    [DataRow(ProfileType.Image, true, false)]
    [DataRow(ProfileType.Image, false, true)]
    [DataRow(ProfileType.Image, true, true)]
    [DataRow(ProfileType.Text, false, false)]
    [DataRow(ProfileType.Text, true, false)]
    [DataRow(ProfileType.Text, false, true)]
    [DataRow(ProfileType.Text, true, true)]
    [DataRow(ProfileType.Group, false, false)]
    [DataRow(ProfileType.Group, true, false)]
    [DataRow(ProfileType.Group, false, true)]
    [DataRow(ProfileType.Group, true, true)]
    public async Task ExistingDataIsReplacedAndBoundToPersistentPath(ProfileType type, bool suppliedHash, bool inPlace)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-Replacement-");
        try
        {
            var persistentDir = Path.Combine(directory.FullName, "persistent");
            var (profile, targetPath) = await CreateProfileWithData(directory.FullName, persistentDir, type, token);
            var expectedHash = await profile.GetHash(token);
            var data = await File.ReadAllBytesAsync(targetPath, token);
            if (profile is GroupProfile group)
                await File.WriteAllTextAsync(group.Files.Single(), "modified extracted file", token);
            await File.WriteAllTextAsync(targetPath, "modified transfer data", token);
            var incomingDir = Directory.CreateDirectory(Path.Combine(directory.FullName, "incoming")).FullName;
            var incomingPath = inPlace ? targetPath : Path.Combine(incomingDir, Path.GetFileName(targetPath));
            await File.WriteAllBytesAsync(incomingPath, data, token);

            await SetAndMove(profile, persistentDir, incomingPath, suppliedHash, token);

            Assert.AreEqual(expectedHash, await profile.GetHash(token));
            Assert.AreEqual(await Utility.CalculateFileSHA256(targetPath, token), profile.TransferDataHash);
            Assert.AreEqual(targetPath, (await profile.PrepareTransferData(persistentDir, token))?.Path);
            Assert.IsTrue(await profile.IsLocalDataValid(false, token));
            CollectionAssert.AreEqual(data, await File.ReadAllBytesAsync(targetPath, token));
            if (!inPlace)
                Assert.IsFalse(File.Exists(incomingPath));
            if (profile is GroupProfile restoredGroup)
            {
                Assert.AreEqual(Path.Combine(targetPath[..^4], "source.txt"), restoredGroup.Files.Single());
                Assert.AreEqual(Content, await File.ReadAllTextAsync(restoredGroup.Files.Single(), token));
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    [DataRow(ProfileType.File, false)]
    [DataRow(ProfileType.File, true)]
    [DataRow(ProfileType.Image, false)]
    [DataRow(ProfileType.Image, true)]
    [DataRow(ProfileType.Text, false)]
    [DataRow(ProfileType.Text, true)]
    [DataRow(ProfileType.Group, false)]
    [DataRow(ProfileType.Group, true)]
    public async Task ExistingDataDoesNotBypassValidationOfIncomingData(ProfileType type, bool suppliedHash)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-Replacement-");
        try
        {
            var persistentDir = Path.Combine(directory.FullName, "persistent");
            var (profile, targetPath) = await CreateProfileWithData(directory.FullName, persistentDir, type, token);
            var originalHash = profile.TransferDataHash;
            var originalData = await File.ReadAllBytesAsync(targetPath, token);
            var incomingDir = Directory.CreateDirectory(Path.Combine(directory.FullName, "incoming")).FullName;
            var incomingPath = Path.Combine(incomingDir, Path.GetFileName(targetPath));
            await File.WriteAllTextAsync(incomingPath, "invalid replacement", token);

            await Assert.ThrowsAsync<Exception>(() => SetAndMove(profile, persistentDir, incomingPath, suppliedHash, token));

            Assert.AreEqual(originalHash, profile.TransferDataHash);
            Assert.IsTrue(await profile.IsLocalDataValid(false, token));
            CollectionAssert.AreEqual(originalData, await File.ReadAllBytesAsync(targetPath, token));
            Assert.IsTrue(File.Exists(incomingPath));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task GroupReplacementDoesNotOverwriteUnownedExtractionDirectory()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-Replacement-");
        try
        {
            var persistentDir = Path.Combine(directory.FullName, "persistent");
            var (profile, targetPath) = await CreateProfileWithData(directory.FullName, persistentDir, ProfileType.Group, token);
            var originalData = await File.ReadAllBytesAsync(targetPath, token);
            File.Delete(Path.Combine(targetPath[..^4], ".syncclipboard-extraction-owner"));
            var unrelatedFile = Path.Combine(targetPath[..^4], "unrelated.txt");
            await File.WriteAllTextAsync(unrelatedFile, "keep", token);
            var incomingDir = Directory.CreateDirectory(Path.Combine(directory.FullName, "incoming")).FullName;
            var incomingPath = Path.Combine(incomingDir, Path.GetFileName(targetPath));
            File.Copy(targetPath, incomingPath);

            await Assert.ThrowsAsync<InvalidDataException>(() => SetAndMove(profile, persistentDir, incomingPath, true, token));

            Assert.AreEqual("keep", await File.ReadAllTextAsync(unrelatedFile, token));
            CollectionAssert.AreEqual(originalData, await File.ReadAllBytesAsync(targetPath, token));
            Assert.IsTrue(File.Exists(incomingPath));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static readonly string Content = new('T', 20000);

    private static async Task<(Profile Profile, string Path)> CreateProfileWithData(
        string directory, string persistentDir, ProfileType type, CancellationToken token)
    {
        var sourcePath = Path.Combine(directory, "source.txt");
        await File.WriteAllTextAsync(sourcePath, Content, token);
        Profile source = type switch
        {
            ProfileType.File => new FileProfile(sourcePath),
            ProfileType.Image => new ImageProfile(sourcePath),
            ProfileType.Text => new TextProfile(Content),
            _ => new GroupProfile([sourcePath]),
        };
        var sourceData = (await source.PrepareTransferData(persistentDir, token))?.Path;
        Assert.IsNotNull(sourceData);
        var dto = await source.ToProfileDto(token);
        Profile profile = type switch
        {
            ProfileType.File => new FileProfile(dto),
            ProfileType.Image => new ImageProfile(dto),
            ProfileType.Text => new TextProfile(dto),
            _ => new GroupProfile(dto),
        };
        var targetPath = profile.GetTransferDataSavePath(persistentDir);
        Assert.IsNotNull(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        if (sourceData != targetPath)
            File.Copy(sourceData, targetPath);
        await profile.SetTransferData(targetPath, true, token);
        return (profile, targetPath);
    }

    private static async Task SetAndMove(Profile profile, string persistentDir, string path, bool suppliedHash, CancellationToken token)
    {
        if (suppliedHash)
        {
            var actualHash = await Utility.VerifyFileSHA256(path, null, token);
            await profile.SetAndMoveTransferData(persistentDir, path, actualHash, token);
        }
        else
        {
            await profile.SetAndMoveTransferData(persistentDir, path, token);
        }
    }
}
