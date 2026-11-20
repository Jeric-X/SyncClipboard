using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test.Client.Clipboard;

[TestClass]
public class TextFilterTests
{
    private const string NautilusClipboard = "x-special/nautilus-clipboard\ncopy\nfile:///tmp/image.png";

    [TestMethod]
    public void BlackList_KeywordBlocksUpload_OnlyMarkedRulesExcludeLocalHistory()
    {
        var config = new TextFilterConfig
        {
            FilterMode = "BlackList",
            BlackList = [new("x-special/nautilus-clipboard"), new(@"(?m)^file:///", UseRegex: true, ExcludeFromHistory: true)],
        };

        Assert.IsFalse(config.AllowsUpload(NautilusClipboard));
        Assert.IsTrue(config.ExcludesFromHistory(NautilusClipboard));
        Assert.IsFalse(config.ExcludesFromHistory("x-special/nautilus-clipboard"));
        Assert.IsFalse(config.AllowsUpload("prefix x-special/nautilus-clipboard suffix"));
        Assert.IsTrue(config.AllowsUpload("ordinary clipboard text"));
    }

    [TestMethod]
    public void WhiteList_RequiresAMatch_AndDisabledFilterAllowsAllText()
    {
        var config = new TextFilterConfig
        {
            FilterMode = "WhiteList",
            WhiteList = [new(@"^ticket-\d+$", UseRegex: true)],
            BlackList = [new("file:///", ExcludeFromHistory: true)],
        };

        Assert.IsTrue(config.AllowsUpload("ticket-42"));
        Assert.IsFalse(config.AllowsUpload(NautilusClipboard));
        Assert.IsFalse(config.ExcludesFromHistory(NautilusClipboard));
        var disabled = config with { FilterMode = "" };
        Assert.IsTrue(disabled.AllowsUpload(NautilusClipboard));
        Assert.IsFalse(disabled.ExcludesFromHistory(NautilusClipboard));
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
        Assert.IsTrue(await ContentControlHelper.IsTextAllowed(profile, config, CancellationToken.None));

        var directory = Directory.CreateTempSubdirectory();
        try
        {
            await profile.Persist(directory.FullName, CancellationToken.None);
            Assert.IsTrue(await ContentControlHelper.IsTextAllowed(profile, config, CancellationToken.None));
            Assert.IsTrue(await profile.TryLocalize(directory.FullName, token: CancellationToken.None));
            Assert.IsTrue(await ContentControlHelper.IsTextAllowed(profile, config, CancellationToken.None));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void RegexValidation_AllowsLiteralPunctuation_AndRejectsInvalidExpressions()
    {
        Assert.IsNull(new TextFilterRule("[").Validate());
        Assert.IsNotNull(new TextFilterRule("[", UseRegex: true).Validate());
    }
}
