using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Utilities;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test;

[TestClass]
public class FileFilterHelperTests
{
    [TestMethod]
    public void BlackList_SupportsMixedSuffixAndRegexRules()
    {
        var config = new FileFilterConfig
        {
            FileFilterMode = "BlackList",
            BlackList =
            [
                new FileFilterRule { Pattern = ".tmp", MatchMode = FileFilterMatchMode.Suffix },
                new FileFilterRule { Pattern = @"^cache-\d+\.dat$", MatchMode = FileFilterMatchMode.Regex },
            ],
        };

        Assert.IsFalse(FileFilterHelper.IsFileAvailableAfterFilter("folder/FILE.TMP", config));
        Assert.IsFalse(FileFilterHelper.IsFileAvailableAfterFilter(@"folder\cache-42.dat", config));
        Assert.IsTrue(FileFilterHelper.IsFileAvailableAfterFilter(@"folder\CACHE-42.dat", config));
        Assert.IsTrue(FileFilterHelper.IsFileAvailableAfterFilter("folder/cache-final.dat", config));
    }

    [TestMethod]
    public void WhiteList_AllowsAnyMatchingRuleAndRejectsOthers()
    {
        var config = new FileFilterConfig
        {
            FileFilterMode = "WhiteList",
            WhiteList =
            [
                new FileFilterRule { Pattern = ".png", MatchMode = FileFilterMatchMode.Suffix },
                new FileFilterRule { Pattern = @"^report-\d{4}\.csv$", MatchMode = FileFilterMatchMode.Regex },
            ],
        };

        Assert.IsTrue(FileFilterHelper.IsFileAvailableAfterFilter("IMAGE.PNG", config));
        Assert.IsTrue(FileFilterHelper.IsFileAvailableAfterFilter("reports/report-2026.csv", config));
        Assert.IsFalse(FileFilterHelper.IsFileAvailableAfterFilter("reports/REPORT-2026.csv", config));
        Assert.IsFalse(FileFilterHelper.IsFileAvailableAfterFilter("report-final.csv", config));
    }

    [TestMethod]
    public void EmptyWhiteList_PreservesExistingRejectAllBehavior()
    {
        var config = new FileFilterConfig { FileFilterMode = "WhiteList" };

        Assert.IsFalse(FileFilterHelper.IsFileAvailableAfterFilter("file.txt", config));
    }

    [TestMethod]
    public void SuffixRules_MatchFullPathAndTrimPattern()
    {
        var config = new FileFilterConfig
        {
            FileFilterMode = "BlackList",
            BlackList =
            [
                new FileFilterRule
                {
                    Pattern = " private/secret.txt ",
                    MatchMode = FileFilterMatchMode.Suffix,
                },
            ],
        };

        Assert.IsFalse(FileFilterHelper.IsFileAvailableAfterFilter("root/private/secret.txt", config));
        Assert.IsTrue(FileFilterHelper.IsFileAvailableAfterFilter("root/public/secret.txt", config));
    }

    [TestMethod]
    public void TryValidateRule_RejectsEmptyAndInvalidRegexPatterns()
    {
        Assert.IsFalse(FileFilterHelper.TryValidateRule(
            new FileFilterRule { Pattern = " " },
            out _));
        Assert.IsFalse(FileFilterHelper.TryValidateRule(
            new FileFilterRule { Pattern = "(", MatchMode = FileFilterMatchMode.Regex },
            out _));
        Assert.IsTrue(FileFilterHelper.TryValidateRule(
            new FileFilterRule { Pattern = @"^file\.txt$", MatchMode = FileFilterMatchMode.Regex },
            out _));
    }

    [TestMethod]
    public void TryValidateRule_DoesNotPopulateCompiledRegexCache()
    {
        var cacheField = typeof(FileFilterHelper).GetField("RegexCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(cacheField);
        var cache = cacheField.GetValue(null) as ConcurrentDictionary<string, Regex>;
        Assert.IsNotNull(cache);
        var pattern = $"^validation-{Guid.NewGuid():N}$";

        Assert.IsTrue(FileFilterHelper.TryValidateRule(
            new FileFilterRule { Pattern = pattern, MatchMode = FileFilterMatchMode.Regex },
            out _));

        Assert.IsFalse(cache.ContainsKey(pattern));
    }

    [TestMethod]
    public void RegexTimeout_RejectsFileWithoutThrowing()
    {
        var config = new FileFilterConfig
        {
            FileFilterMode = "BlackList",
            BlackList =
            [
                new FileFilterRule
                {
                    Pattern = "^(a|aa)+$",
                    MatchMode = FileFilterMatchMode.Regex,
                },
            ],
        };
        var fileName = new string('a', 10_000) + "!";

        Assert.IsFalse(FileFilterHelper.IsFileAvailableAfterFilter(fileName, config));
    }

    [TestMethod]
    public void RuleEditors_PreserveRegexWhitespaceAndTrimSuffixWhitespace()
    {
        const string regexPattern = @" ^report\.txt$ ";
        var regexRule = new FileFilterRule
        {
            Pattern = regexPattern,
            MatchMode = FileFilterMatchMode.Regex,
        };

        Assert.AreEqual(regexPattern, new FileFilterRuleEditor(regexRule).ToRule().Pattern);
        Assert.AreEqual(regexPattern, new EditableFileFilterRule(regexRule).ToRule().Pattern);
        Assert.AreEqual(
            ".tmp",
            new FileFilterRuleEditor(new FileFilterRule { Pattern = " .tmp " }).ToRule().Pattern);
    }
}
