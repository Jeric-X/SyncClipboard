using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.UserServices.ClipboardService;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryFileFilterTests
{
    private static readonly string[] SelectedFileNames = ["private.tmp", "ordinary.log"];

    [TestMethod]
    [DataRow("BlackList")]
    [DataRow("WhiteList")]
    public void ActiveRules_ExcludeOnlyMatchingFilesMarkedForHistory(string mode)
    {
        var config = CreateConfig(mode);
        Assert.IsFalse(ShouldRecord("private.TMP", config));
        Assert.IsFalse(ShouldRecord("secret-42.dat", config));
        Assert.IsTrue(ShouldRecord("ordinary.log", config));
        Assert.IsTrue(ShouldRecord("public.txt", config));
        Assert.IsTrue(ShouldRecord("SECRET-42.dat", config));

        config.FileFilterMode = "";
        Assert.IsTrue(ShouldRecord("private.TMP", config));
    }

    [TestMethod]
    public void InactiveRules_DoNotExcludeFilesFromHistory()
    {
        var config = CreateConfig("BlackList");
        config.FileFilterMode = "WhiteList";
        Assert.IsTrue(ShouldRecord("private.tmp", config));
    }

    [TestMethod]
    public void TextAndImages_UseUploadFileFilterExemptions()
    {
        var config = new FileFilterConfig
        {
            FileFilterMode = "BlackList",
            BlackList = [new() { Pattern = ".png", ExcludeFromHistory = true }],
        };
        Assert.IsTrue(HistoryService.ShouldRecordLocalProfile(new TextProfile("image.png"), config));
        Assert.IsTrue(HistoryService.ShouldRecordLocalProfile(new ImageProfile(null, "image.png"), config));
        Assert.IsFalse(ShouldRecord("image.png", config));
    }

    [TestMethod]
    public void Group_UsesUploadAnyAvailableFileDecision()
    {
        var config = CreateConfig("WhiteList");
        var files = SelectedFileNames.Select(name => Path.Combine(Path.GetTempPath(), name)).ToArray();
        var group = new GroupProfile(files, config);

        Assert.IsTrue(HistoryService.ShouldRecordLocalProfile(group, config));
        CollectionAssert.AreEqual(files, group.Files);
        Assert.IsFalse(HistoryService.ShouldRecordLocalProfile(new GroupProfile(files[..1], config), config));
        Assert.IsFalse(HistoryService.ShouldRecordLocalProfile(new GroupProfile([]), config));
    }

    [TestMethod]
    public void Directory_UsesUploadDirectoryDecision()
    {
        var directory = Directory.CreateTempSubdirectory("HistoryFileFilterTests-").FullName;
        try
        {
            var config = CreateConfig("BlackList");
            File.WriteAllText(Path.Combine(directory, "private.tmp"), "private");
            var group = new GroupProfile([directory], config);
            Assert.IsTrue(ContentControlHelper.IsFileAllowed(group, config));
            Assert.IsTrue(HistoryService.ShouldRecordLocalProfile(group, config));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool ShouldRecord(string name, FileFilterConfig config) =>
        HistoryService.ShouldRecordLocalProfile(new FileProfile(null, name), config);

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
