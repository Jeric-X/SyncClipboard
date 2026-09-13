using SyncClipboard.Shared;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class SetTransferDataInfoTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(ProfileType.File, false)]
    [DataRow(ProfileType.File, true)]
    [DataRow(ProfileType.Image, false)]
    [DataRow(ProfileType.Image, true)]
    [DataRow(ProfileType.Text, false)]
    [DataRow(ProfileType.Text, true)]
    [DataRow(ProfileType.Group, false)]
    [DataRow(ProfileType.Group, true)]
    public async Task SetTransferData_BindsFileInfoAndPreservesVerification(ProfileType type, bool verify)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-SetFileInfo-");
        try
        {
            var (profile, file) = await CreateIncomingFile(directory.FullName, type, token);

            await profile.SetTransferData(file, verify, token);

            Assert.AreEqual(file.Hash, profile.TransferDataHash);
            Assert.IsTrue(await Utility.FileMatchesSHA256(file.Path, profile.TransferDataHash, token));
            if (profile is GroupProfile group)
                Assert.AreEqual(verify, group.Files?.Length > 0);
            Assert.AreEqual(file, await profile.PrepareTransferData(directory.FullName, token));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    [DataRow(ProfileType.File)]
    [DataRow(ProfileType.Image)]
    [DataRow(ProfileType.Text)]
    [DataRow(ProfileType.Group)]
    public async Task OverloadsPreserveHashAndVerificationSemantics(ProfileType type)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-PathOverloads-");
        try
        {
            var (profile, file) = await CreateIncomingFile(directory.FullName, type, token);
            var expectedProfileHash = await profile.GetHash(token);

            await profile.SetTransferData(file.Path, false, token);
            Assert.IsNull(profile.TransferDataHash);
            if (profile is GroupProfile)
                Assert.IsFalse(Directory.Exists(file.Path[..^4]));

            await profile.SetTransferData(file with { Hash = file.Hash.ToLowerInvariant() }, false, token);
            Assert.AreEqual(file.Hash, profile.TransferDataHash);
            if (profile is GroupProfile)
                Assert.IsFalse(Directory.Exists(file.Path[..^4]));

            await profile.SetTransferData(file.Path, true, token);
            Assert.AreEqual(file.Hash, profile.TransferDataHash);
            Assert.AreEqual(expectedProfileHash, await profile.GetHash(token));
            Assert.IsTrue(await profile.IsLocalDataValid(false, token));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    [DataRow(ProfileType.File)]
    [DataRow(ProfileType.Image)]
    [DataRow(ProfileType.Text)]
    [DataRow(ProfileType.Group)]
    public async Task SetAndMoveTransferData_MovesFileInfoToPersistentDirectory(ProfileType type)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-MoveFileInfo-");
        try
        {
            var (profile, file) = await CreateIncomingFile(directory.FullName, type, token);
            var persistentDir = Path.Combine(directory.FullName, "persistent");
            var target = profile.GetTransferDataSavePath(persistentDir);

            await profile.SetAndMoveTransferData(persistentDir, file, token);

            Assert.IsFalse(File.Exists(file.Path));
            Assert.IsTrue(File.Exists(target));
            Assert.AreEqual(file.Hash, profile.TransferDataHash);
            Assert.IsTrue(await profile.IsLocalDataValid(false, token));
            Assert.AreEqual(target, (await profile.PrepareTransferData(persistentDir, token))?.Path);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task SetTransferData_DoesNotVerifySuppliedFileHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-FileInfoHash-");
        try
        {
            var path = Path.Combine(directory.FullName, "text.txt");
            await File.WriteAllTextAsync(path, "actual", token);
            var suppliedHash = new string('A', 64);
            var profile = new TextProfile(new ProfileDto
            {
                Type = ProfileType.Text,
                Hash = suppliedHash,
                HasData = true,
                DataName = Path.GetFileName(path),
            });

            await profile.SetTransferData(new FileHashInfo(path, suppliedHash), true, token);

            Assert.AreEqual(suppliedHash, profile.TransferDataHash);
            Assert.IsFalse(await Utility.FileMatchesSHA256(path, profile.TransferDataHash, token));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    [DataRow(ProfileType.File)]
    [DataRow(ProfileType.Image)]
    [DataRow(ProfileType.Text)]
    [DataRow(ProfileType.Group)]
    public async Task CopiedProfileRetainsHashAndRechecksCurrentFile(ProfileType type)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-CopyFileHash-");
        try
        {
            var (profile, file) = await CreateIncomingFile(directory.FullName, type, token);
            await profile.SetTransferData(file, false, token);
            var copy = Profile.Create(await profile.ToProfileDto(token));
            profile.CopyTo(copy);

            Assert.AreEqual(file.Hash, copy.TransferDataHash);
            Assert.IsTrue(await profile.IsDataComplete(false, token));
            Assert.IsTrue(await copy.IsDataComplete(false, token));

            await File.WriteAllTextAsync(file.Path, "modified", token);

            Assert.IsFalse(await profile.IsDataComplete(false, token));
            Assert.IsFalse(await copy.IsDataComplete(false, token));
            Assert.AreEqual(file.Hash, profile.TransferDataHash);
            Assert.AreEqual(file.Hash, copy.TransferDataHash);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static async Task<(Profile Profile, FileHashInfo File)> CreateIncomingFile(
        string directory, ProfileType type, CancellationToken token)
    {
        var sourcePath = Path.Combine(directory, "source.txt");
        var content = new string('T', 20000);
        await File.WriteAllTextAsync(sourcePath, content, token);
        Profile source = type switch
        {
            ProfileType.File => new FileProfile(sourcePath),
            ProfileType.Image => new ImageProfile(sourcePath),
            ProfileType.Text => new TextProfile(content),
            _ => new GroupProfile([sourcePath]),
        };
        var file = await source.PrepareTransferData(directory, token);
        Assert.IsNotNull(file);
        var incomingDir = Directory.CreateDirectory(Path.Combine(directory, "incoming")).FullName;
        var path = Path.Combine(incomingDir, Path.GetFileName(file.Path));
        File.Copy(file.Path, path);
        return (Profile.Create(await source.ToProfileDto(token)), new FileHashInfo(path, file.Hash));
    }
}
