using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test.Client.Clipboard;

[TestClass]
public class TextFilterTests
{
    private const string NautilusClipboard = "x-special/nautilus-clipboard\ncopy\nfile:///tmp/image.png";

    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    public async Task BlackList_KeywordBlocksUpload_OnlyMarkedRulesExcludeLocalHistory()
    {
        var config = new TextFilterConfig
        {
            FilterMode = "BlackList",
            BlackList = [new("x-special/nautilus-clipboard"), new(@"(?m)^file:///", UseRegex: true, ExcludeFromHistory: true)],
        };

        Assert.IsFalse(await TextFilterHelper.AllowsUploadAsync(new(NautilusClipboard), config, Token));
        Assert.IsTrue(TextFilterHelper.ExcludesFromHistory(NautilusClipboard, config));
        Assert.IsFalse(TextFilterHelper.ExcludesFromHistory("x-special/nautilus-clipboard", config));
        Assert.IsFalse(await TextFilterHelper.AllowsUploadAsync(new("prefix x-special/nautilus-clipboard suffix"), config, Token));
        Assert.IsTrue(await TextFilterHelper.AllowsUploadAsync(new("ordinary clipboard text"), config, Token));
    }

    [TestMethod]
    public async Task WhiteList_RequiresAMatch_AndDisabledFilterAllowsAllText()
    {
        var config = new TextFilterConfig
        {
            FilterMode = "WhiteList",
            WhiteList = [new(@"^ticket-\d+$", UseRegex: true)],
            BlackList = [new("file:///", ExcludeFromHistory: true)],
        };

        Assert.IsTrue(await TextFilterHelper.AllowsUploadAsync(new("ticket-42"), config, Token));
        Assert.IsFalse(await TextFilterHelper.AllowsUploadAsync(new(NautilusClipboard), config, Token));
        Assert.IsFalse(TextFilterHelper.ExcludesFromHistory(NautilusClipboard, config));
        var disabled = config with { FilterMode = "" };
        Assert.IsTrue(await TextFilterHelper.AllowsUploadAsync(new(NautilusClipboard), disabled, Token));
        Assert.IsFalse(TextFilterHelper.ExcludesFromHistory(NautilusClipboard, disabled));
    }

    [TestMethod]
    public async Task UploadChecksTextBeyondThePreview_IncludingAfterPersistence()
    {
        var config = new TextFilterConfig
        {
            FilterMode = "WhiteList",
            WhiteList = [new(@"\A\uFEFFa+private-marker\z", UseRegex: true)],
        };
        var profile = new TextProfile("\uFEFF" + new string('a', 12000) + "private-marker");
        Assert.IsTrue(await TextFilterHelper.AllowsUploadAsync(profile, config, Token));

        var directory = Directory.CreateTempSubdirectory();
        try
        {
            await profile.Persist(directory.FullName, Token);
            Assert.IsTrue(await TextFilterHelper.AllowsUploadAsync(profile, config, Token));
            Assert.IsTrue(await profile.TryLocalize(directory.FullName, token: Token));
            Assert.IsTrue(await TextFilterHelper.AllowsUploadAsync(profile, config, Token));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task ClipboardTextCanBeCheckedWithoutReadingTheHistoryAttachment()
    {
        var text = new string('a', 12000) + "private-marker";
        var profile = new TextProfile(text);
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            await profile.Persist(directory.FullName, Token);
        }
        finally
        {
            directory.Delete(true);
        }

        var config = new TextFilterConfig { FilterMode = "BlackList", BlackList = [new("private-marker")] };
        Assert.IsFalse(await TextFilterHelper.AllowsUploadAsync(profile, config, Token, text));
        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() =>
            TextFilterHelper.AllowsUploadAsync(profile, config, Token));
    }

    [TestMethod]
    public void RegexValidation_AllowsLiteralPunctuation_AndRejectsInvalidExpressions()
    {
        Assert.IsNull(TextFilterHelper.ValidateRule(new("[")));
        Assert.IsNotNull(TextFilterHelper.ValidateRule(new("[", UseRegex: true)));
    }
}
