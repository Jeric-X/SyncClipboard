using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class PrepareTransferDataInfoTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(ProfileType.File)]
    [DataRow(ProfileType.Image)]
    [DataRow(ProfileType.Text)]
    [DataRow(ProfileType.Group)]
    public async Task PreparedAndReusedFileReturnMatchingPathAndHash(ProfileType type)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-PreparedFile-");
        try
        {
            var path = Path.Combine(directory.FullName, "source.txt");
            await File.WriteAllTextAsync(path, "content", token);
            Profile profile = type switch
            {
                ProfileType.File => new FileProfile(path),
                ProfileType.Image => new ImageProfile(path),
                ProfileType.Text => new TextProfile(new string('T', 20000)),
                _ => new GroupProfile([path]),
            };

            var file = await profile.PrepareTransferData(directory.FullName, token);

            Assert.IsNotNull(file);
            Assert.IsTrue(File.Exists(file.Path));
            Assert.AreEqual(await Utility.CalculateFileSHA256(file.Path, token), file.Hash);
            Assert.AreEqual(file.Hash, profile.TransferDataHash);
            var info = await profile.Persist(directory.FullName, token);
            var restored = Profile.Create(directory.FullName, info);

            var reused = await restored.PrepareTransferData(directory.FullName, token);

            Assert.AreEqual(file, reused);
            Assert.AreEqual(file.Hash, restored.TransferDataHash);
            Assert.AreEqual(file.Hash, (await restored.ToProfileDto(token)).TransferDataHash);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task InlineTextReturnsNoFile()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var profile = new TextProfile("inline");

        Assert.IsNull(await profile.PrepareTransferData(Path.GetTempPath(), token));
        Assert.IsNull(profile.TransferDataHash);
    }
}
