using System.IO.Compression;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
[TestCategory("NonUI")]
public class ProfileLocalizationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(ProfileType.File, false)]
    [DataRow(ProfileType.File, true)]
    [DataRow(ProfileType.Image, false)]
    [DataRow(ProfileType.Image, true)]
    [DataRow(ProfileType.Text, false)]
    [DataRow(ProfileType.Text, true)]
    public async Task Localize_DoesNotValidateFileContent(ProfileType type, bool hasTransferHash)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "data.txt");
            await File.WriteAllTextAsync(path, "current content", token);
            var expectedHash = new string('0', 64);
            var transferHash = hasTransferHash ? expectedHash : null;
            var info = new ProfilePersistentInfo
            {
                Type = type,
                Text = "preview",
                Size = 10241,
                Hash = expectedHash,
                TransferDataFile = path,
                TransferDataHash = transferHash,
                FilePaths = [path],
            };
            Profile profile = type switch
            {
                ProfileType.Text => new TextProfile(info),
                ProfileType.Image => new ImageProfile(info),
                _ => new FileProfile(info),
            };

            var localInfo = await profile.Localize(directory, token);

            if (type == ProfileType.Text)
                Assert.AreEqual("current content", localInfo.Text);
            else
                CollectionAssert.AreEqual(new[] { path }, localInfo.FilePaths);
            Assert.AreEqual(expectedHash, await profile.GetHash(token));
            Assert.AreEqual(transferHash, profile.TransferDataHash);
            Assert.IsFalse(await profile.IsDataComplete(false, token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Group_LocalizeExtractsWithoutValidatingOrCreatingHashes(bool hasHashes)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "data.zip");
            await CreateArchive(path, "data.txt", token);
            var expectedHash = hasHashes ? new string('0', 64) : null;
            var profile = new InspectableGroupProfile(path, expectedHash);

            var localInfo = await profile.Localize(directory, token);

            Assert.AreEqual("content", await File.ReadAllTextAsync(localInfo.FilePaths.Single(), token));
            Assert.AreEqual(expectedHash, profile.TransferDataHash);
            Assert.AreEqual(expectedHash, profile.ProfileHash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Group_LocalizationPreservesTransferDataBinding(bool tryLocalize)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "data.zip");
            await CreateArchive(path, "data.txt", token);
            var transferHash = await Utility.CalculateFileSHA256(path, token);
            var profile = new GroupProfile(new ProfilePersistentInfo
            {
                Type = ProfileType.Group,
                Hash = new string('A', 64),
                Text = "data.txt",
                Size = 7,
                FilePaths = [],
                TransferDataFile = path,
                TransferDataHash = transferHash,
            });
            var before = await profile.Persist(directory, token);
            var beforeDto = await profile.ToProfileDto(token);

            if (tryLocalize)
                Assert.IsTrue(await profile.TryLocalize(directory, false, token));
            else
                await profile.Localize(directory, token);

            var after = await profile.Persist(directory, token);
            Assert.AreEqual(before.TransferDataFile, after.TransferDataFile);
            Assert.AreEqual(before.TransferDataHash, after.TransferDataHash);
            Assert.AreEqual(before.Hash, after.Hash);
            Assert.AreEqual(before.Size, after.Size);
            Assert.AreEqual(beforeDto.DataName, (await profile.ToProfileDto(token)).DataName);
            Assert.AreEqual("content", await File.ReadAllTextAsync(profile.Files.Single(), token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Group_LocalizeStillRejectsPathTraversal()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "data.zip");
            await CreateArchive(path, "../outside.txt", token);
            var profile = new GroupProfile([], string.Empty, path);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => profile.Localize(directory, token));

            Assert.IsFalse(File.Exists(Path.Combine(directory, "outside.txt")));
            Assert.IsFalse(Directory.EnumerateDirectories(directory).Any());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Group_LocalizeWithoutProfileHashUsesSeparateExtractionDirectories()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "data.zip");
            await CreateArchive(path, "data.txt", token);
            var firstProfile = new GroupProfile([], string.Empty, path);
            var localInfo = await firstProfile.Localize(directory, token);
            var secondProfile = new GroupProfile([], string.Empty, path);

            var secondLocalInfo = await secondProfile.Localize(directory, token);

            Assert.AreNotEqual(localInfo.FilePaths.Single(), secondLocalInfo.FilePaths.Single());
            Assert.AreEqual("content", await File.ReadAllTextAsync(secondLocalInfo.FilePaths.Single(), token));
            Assert.AreEqual("content", await File.ReadAllTextAsync(localInfo.FilePaths.Single(), token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(5, false)]
    [DataRow(10241, false)]
    [DataRow(20000, true)]
    public async Task Text_LocalizeAlwaysReturnsFullText(int length, bool restoredFromDisk)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = CreateTestDirectory();
        try
        {
            var text = new string('T', length);
            Profile profile = new TextProfile(text);
            if (restoredFromDisk)
            {
                var info = await profile.Persist(directory, token);
                profile = Profile.Create(directory, info);
            }

            var localInfo = await profile.Localize(directory, token);

            Assert.AreEqual(text, localInfo.Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("data.txt")]
    [DataRow("./data.txt")]
    [DataRow("DATA.txt")]
    public async Task Group_LocalizeRejectsDestinationFileCollisions(string secondEntryName)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = CreateTestDirectory();
        try
        {
            // Probe the actual filesystem so case-sensitive volumes can still extract distinct names.
            var probePath = Path.Combine(directory, "data.txt");
            await File.WriteAllTextAsync(probePath, "probe", token);
            var pathsCollide = File.Exists(Path.Combine(directory, secondEntryName));
            File.Delete(probePath);

            var archivePath = Path.Combine(directory, "data.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("data.txt").Open()))
                    await writer.WriteAsync("first".AsMemory(), token);
                using (var writer = new StreamWriter(archive.CreateEntry(secondEntryName).Open()))
                    await writer.WriteAsync("second".AsMemory(), token);
            }

            var transferHash = await Utility.CalculateFileSHA256(archivePath, token);
            var profile = new GroupProfile([], new string('A', 64));
            await profile.SetTransferData(new FileHashInfo(archivePath, transferHash), false, token);

            if (pathsCollide)
            {
                await Assert.ThrowsExactlyAsync<IOException>(() => profile.Localize(directory, token));
                Assert.AreEqual(0, profile.Files.Length);
                Assert.AreEqual(0, Directory.GetDirectories(directory).Length);
            }
            else
            {
                var localInfo = await profile.Localize(directory, token);
                Assert.AreEqual(2, localInfo.FilePaths.Length);
                Assert.AreEqual("first", await File.ReadAllTextAsync(localInfo.FilePaths[0], token));
                Assert.AreEqual("second", await File.ReadAllTextAsync(localInfo.FilePaths[1], token));
            }

            Assert.IsTrue(File.Exists(archivePath));
            Assert.AreEqual(transferHash, profile.TransferDataHash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class InspectableGroupProfile(string path, string? hash) : GroupProfile([], hash ?? string.Empty, path, hash)
    {
        public string? ProfileHash => Hash;
    }

    private static async Task CreateArchive(string path, string entryName, CancellationToken token)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        await using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
        await writer.WriteAsync("content".AsMemory(), token);
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SyncClipboard-Localization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
