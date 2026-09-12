using SyncClipboard.Shared;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;

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
            Assert.IsTrue(await profile.IsTransferDataValid(token));
            if (profile is GroupProfile)
                Assert.AreEqual(verify, Directory.Exists(file.Path[..^4]));
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
            Assert.IsFalse(await profile.IsTransferDataValid(token));
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
