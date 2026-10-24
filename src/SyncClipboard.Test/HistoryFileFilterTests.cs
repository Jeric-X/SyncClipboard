using System.IO.Compression;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.UserServices.ClipboardService;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryFileFilterTests
{
    private static readonly string[] SelectedFileNames = ["private.tmp", "ordinary.log", "public.txt"];
    private static readonly string[] ExpectedArchiveFileNames = ["ordinary.log", "public.txt"];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("BlackList")]
    [DataRow("WhiteList")]
    public void ActiveRules_ExcludeOnlyMatchingFilesMarkedForHistory(string mode)
    {
        var config = CreateConfig(mode);
        Assert.IsNull(Filter("private.TMP", config));
        Assert.IsNull(Filter("secret-42.dat", config));
        Assert.IsNotNull(Filter("ordinary.log", config));
        Assert.IsNotNull(Filter("public.txt", config));
        Assert.IsNotNull(Filter("SECRET-42.dat", config));

        config.FileFilterMode = "";
        Assert.IsNotNull(Filter("private.TMP", config));
    }

    [TestMethod]
    public void InactiveRules_DoNotExcludeFilesFromHistory()
    {
        var config = CreateConfig("BlackList");
        config.FileFilterMode = "WhiteList";
        Assert.IsNotNull(Filter("private.tmp", config));
    }

    [TestMethod]
    public void TextAndClipboardImages_ArePreservedWhileImageFilesAreFiltered()
    {
        var config = new FileFilterConfig
        {
            FileFilterMode = "BlackList",
            BlackList = [new() { Pattern = ".png", ExcludeFromHistory = true }],
        };
        var image = new ImageProfile(null, "image.png");
        var text = new TextProfile("image.png");

        Assert.AreSame(text, HistoryService.FilterLocalProfile(new(), text, config));
        Assert.AreSame(image, HistoryService.FilterLocalProfile(new(), image, config));
        Assert.IsNull(HistoryService.FilterLocalProfile(new() { Files = ["image.png"] }, image, config));
    }

    [TestMethod]
    public void Group_UsesOriginalSelectionAndDoesNotChangeUploadProfile()
    {
        var config = CreateConfig("BlackList");
        var files = SelectedFileNames
            .Select(name => Path.Combine(Path.GetTempPath(), name)).ToArray();
        var upload = new GroupProfile(files, config);
        var history = HistoryService.FilterLocalProfile(new() { Files = files }, upload, config) as GroupProfile;

        Assert.IsNotNull(history);
        CollectionAssert.AreEqual(files[1..], history.Files);
        CollectionAssert.AreEqual(files[2..], upload.Files);

        files = files[..1];
        Assert.IsNull(HistoryService.FilterLocalProfile(new() { Files = files }, new GroupProfile(files, config), config));
    }

    [TestMethod]
    [DataRow("BlackList", false)]
    [DataRow("BlackList", true)]
    [DataRow("WhiteList", false)]
    [DataRow("WhiteList", true)]
    public async Task HistorySync_DoesNotUploadGroupsContainingSyncExcludedFiles(string mode, bool nested)
    {
        var root = Directory.CreateTempSubdirectory("HistoryUploadFilterTests-").FullName;
        var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var token = TestContext.CancellationTokenSource.Token;
        try
        {
            var allowed = Path.Combine(source, "public.txt");
            var blocked = Path.Combine(source, "private.log");
            await File.WriteAllTextAsync(allowed, "public", token);
            await File.WriteAllTextAsync(blocked, "local history only", token);
            var config = new FileFilterConfig
            {
                FileFilterMode = mode,
                BlackList = [new() { Pattern = ".log" }],
                WhiteList = [new() { Pattern = ".txt" }],
            };
            string[] files = nested ? [source] : [allowed, blocked];
            var history = new GroupProfile(files);
            var saved = await history.Persist(Path.Combine(root, "history"), token);
            var restored = new GroupProfile(saved);

            Assert.IsFalse(await restored.IsAllowedByFileFilterAsync(config, token));
            Assert.IsTrue(await new GroupProfile(files, config).IsAllowedByFileFilterAsync(config, token));
            Assert.IsTrue(await restored.IsAllowedByFileFilterAsync(new FileFilterConfig(), token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UploadFilter_UnreadableGroupIsSkipped(bool accessDenied)
    {
        Exception error = accessDenied ? new UnauthorizedAccessException() : new IOException();
        var profile = new UnreadableGroupProfile(error);

        Assert.IsFalse(await profile.IsAllowedByFileFilterAsync(CreateConfig("BlackList"),
            TestContext.CancellationTokenSource.Token));
    }

    [TestMethod]
    public async Task UploadFilter_CancellationPropagates()
    {
        var profile = new UnreadableGroupProfile(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            profile.IsAllowedByFileFilterAsync(CreateConfig("BlackList"), TestContext.CancellationTokenSource.Token));
    }

    private sealed class UnreadableGroupProfile(Exception error) : GroupProfile([])
    {
        protected override Task ComputeHash(CancellationToken token) => Task.FromException(error);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HistorySync_UsesSavedArchiveWhenOriginalFilesChangeOrDisappear(bool deleteSource)
    {
        var root = Directory.CreateTempSubdirectory("HistoryArchiveFilterTests-").FullName;
        var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var token = TestContext.CancellationTokenSource.Token;
        try
        {
            var file = Path.Combine(source, "public.txt");
            await File.WriteAllTextAsync(file, "saved content", token);
            var profile = new GroupProfile([source]);
            await profile.PrepareTransferData(Path.Combine(root, "history"), token);
            if (deleteSource)
                Directory.Delete(source, recursive: true);
            else
            {
                await File.WriteAllTextAsync(file, "changed content", token);
                await File.WriteAllTextAsync(Path.Combine(source, "private.log"), "new private file", token);
            }

            var config = new FileFilterConfig
            {
                FileFilterMode = "BlackList",
                BlackList = [new() { Pattern = ".log" }],
            };
            Assert.IsTrue(await profile.IsAllowedByFileFilterAsync(config, token));
            config.BlackList = [new() { Pattern = ".txt" }];
            Assert.IsFalse(await profile.IsAllowedByFileFilterAsync(config, token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Directory_HistoryArchiveExcludesMarkedFilesAndKeepsOtherContents()
    {
        var root = Path.Combine(Path.GetTempPath(), $"HistoryFileFilterTests-{Guid.NewGuid():N}");
        var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(source, "nested")).FullName;
        var token = TestContext.CancellationTokenSource.Token;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(source, "private.tmp"), "private", token);
            await File.WriteAllTextAsync(Path.Combine(nested, "secret-42.dat"), "private", token);
            await File.WriteAllTextAsync(Path.Combine(nested, "ordinary.log"), "history only", token);
            await File.WriteAllTextAsync(Path.Combine(nested, "public.txt"), "public", token);
            var config = CreateConfig("BlackList");
            var upload = new GroupProfile([source], config);
            var history = HistoryService.FilterLocalProfile(new() { Files = [source] }, upload, config);
            Assert.IsNotNull(history);

            var archive = await history.PrepareTransferData(Path.Combine(root, "history"), token);
            Assert.IsNotNull(archive);
            using var zip = ZipFile.OpenRead(archive.Path);
            var names = zip.Entries.Where(entry => entry.Name.Length > 0).Select(entry => entry.Name).ToArray();
            CollectionAssert.AreEquivalent(ExpectedArchiveFileNames, names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Profile? Filter(string name, FileFilterConfig config) =>
        HistoryService.FilterLocalProfile(new ClipboardMetaInfomation(), new FileProfile(null, name), config);

    private static FileFilterConfig CreateConfig(string mode)
    {
        var rules = new List<FileFilterRule>
        {
            new() { Pattern = ".tmp", ExcludeFromHistory = true },
            new() { Pattern = @"^secret-\d+\.dat$", MatchMode = FileFilterMatchMode.Regex, ExcludeFromHistory = true },
            new() { Pattern = ".log" },
        };
        return new FileFilterConfig
        {
            FileFilterMode = mode,
            BlackList = mode == "BlackList" ? rules : [],
            WhiteList = mode == "WhiteList" ? rules : [],
        };
    }
}
