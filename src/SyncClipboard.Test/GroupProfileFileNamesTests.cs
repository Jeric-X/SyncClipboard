using SyncClipboard.Shared;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;

namespace SyncClipboard.Test;

[TestClass]
public class GroupProfileFileNamesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LocalConstructorsInitializeNamesBeforePersistence(bool knownHash)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(directory, "file.txt");
            var folderPath = Path.Combine(directory, "folder");
            await File.WriteAllTextAsync(filePath, "content", token);
            Directory.CreateDirectory(folderPath);
            string[] paths = [filePath, folderPath + Path.DirectorySeparatorChar];
            var profile = knownHash
                ? new GroupProfile(paths, new string('A', 64))
                : new GroupProfile(paths);

            var info = await profile.Persist(directory, token);

            Assert.AreEqual("file.txt\nfolder", info.Text);
            Assert.AreEqual(info.Text, profile.DisplayText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LocalConstructorUsesFilteredNames()
    {
        var config = new FileFilterConfig
        {
            FileFilterMode = "BlackList",
            BlackList = [new FileFilterRule { Pattern = ".tmp", MatchMode = FileFilterMatchMode.Suffix }],
        };
        var profile = new GroupProfile(["keep.txt", "skip.tmp"], config);

        Assert.AreEqual("keep.txt", profile.DisplayText);
    }

    [TestMethod]
    public async Task ArchiveOnlyHistoryPreservesAllNamesAcrossPersistence()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = CreateTestDirectory();
        try
        {
            var paths = Enumerable.Range(1, 7).Select(index => Path.Combine(directory, $"file{index}.txt")).ToArray();
            foreach (var path in paths)
                await File.WriteAllTextAsync(path, "content", token);
            var source = new GroupProfile(paths);
            var archivePath = (await source.PrepareTransferData(directory, token))?.Path;
            Assert.IsNotNull(archivePath);
            var info = await source.Persist(directory, token);
            var archiveOnlyInfo = info with { FilePaths = [] };
            var restored = Profile.Create(directory, archiveOnlyInfo);

            var saved = await restored.Persist(directory, token);

            Assert.AreEqual(info.Text, saved.Text);
            Assert.IsEmpty(saved.FilePaths);
            Assert.AreEqual(info.Text, Profile.Create(directory, saved).DisplayText);
            Assert.AreEqual("file1.txt\nfile2.txt\nfile3.txt\nfile4.txt\nfile5.txt\n...", restored.ShortDisplayText);
            Assert.IsFalse(Directory.Exists(archivePath[..^4]));

            var copy = new GroupProfile([]);
            restored.CopyTo(copy);
            Assert.AreEqual(info.Text, copy.DisplayText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("", "file.txt")]
    [DataRow("saved.txt", "saved.txt")]
    public void HistoryConstructorUsesSavedNamesWithLegacyPathFallback(string text, string expected)
    {
        var profile = new GroupProfile(new ProfilePersistentInfo
        {
            Type = ProfileType.Group,
            Text = text,
            Hash = new string('A', 64),
            Size = 1,
            FilePaths = [Path.Combine(Path.GetTempPath(), "file.txt")],
        });

        Assert.AreEqual(expected, profile.DisplayText);
        Assert.AreEqual(expected, profile.ShortDisplayText);
    }

    [TestMethod]
    [DataRow("", "actual.txt")]
    [DataRow("preview.txt", "preview.txt")]
    public async Task ExtractionOnlyFillsUnknownNames(string initialNames, string expectedNames)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "actual.txt");
            await File.WriteAllTextAsync(path, "content", token);
            var source = new GroupProfile([path]);
            var archivePath = (await source.PrepareTransferData(directory, token))?.Path;
            Assert.IsNotNull(archivePath);
            var profile = new GroupProfile(new ProfileDto
            {
                Type = ProfileType.Group,
                Text = initialNames,
                Hash = await source.GetHash(token),
                Size = await source.GetSize(token),
                HasData = true,
            });
            Assert.AreEqual(initialNames, profile.DisplayText);

            await profile.SetTransferData(archivePath, source.TransferDataHash!, verify: false, token);
            await profile.Localize(directory, token);
            var saved = await profile.Persist(directory, token);

            Assert.AreEqual(expectedNames, profile.DisplayText);
            Assert.AreEqual(expectedNames, saved.Text);
            Assert.AreEqual("actual.txt", Path.GetFileName(profile.Files.Single()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SyncClipboard-GroupNames-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
