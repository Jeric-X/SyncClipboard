using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Test.Client.Clipboard;

[TestClass]
public class TextFilterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task MatchingTextIsBlockedFromUploadAndOptionallyExcludedFromHistory()
    {
        var config = new TextFilterConfig
        {
            FilterMode = "BlackList",
            BlackList = [new("local only"), new(@"^OTP-\d{6}$", UseRegex: true, ExcludeFromHistory: true)],
        };
        var token = TestContext.CancellationTokenSource.Token;

        const string keywordMatch = "keep this local only";
        const string regexMatch = "OTP-123456";
        Assert.IsFalse(await TextFilterHelper.AllowsUploadAsync(new(keywordMatch), config, token));
        Assert.IsFalse(TextFilterHelper.ExcludesFromHistory(keywordMatch, config));
        Assert.IsFalse(await TextFilterHelper.AllowsUploadAsync(new(regexMatch), config, token));
        Assert.IsTrue(TextFilterHelper.ExcludesFromHistory(regexMatch, config));
        Assert.IsTrue(await TextFilterHelper.AllowsUploadAsync(new("ordinary text"), config, token));
    }
}
