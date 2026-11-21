using SyncClipboard.Core.Commons;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Shared.Models;

namespace SyncClipboard.Test.Client.Clipboard;

[TestClass]
public class FileSyncFilterSettingViewModelTests
{
    private static readonly string[] ExpectedDistinctPatterns = [".tmp", ".log"];

    private string _directory = null!;
    private string _configPath = null!;
    private ConfigBase _config = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"FileSyncFilterSettingViewModelTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _configPath = Path.Combine(_directory, "SyncClipboard.json");
        File.WriteAllText(_configPath, "{}");
        _config = new ConfigBase(_configPath);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void RuleEditor_SavesAndReloadsHistoryExclusion()
    {
        _config.SetConfig(new FileFilterConfig { FileFilterMode = "BlackList" });
        var viewModel = new FileSyncFilterSettingViewModel(_config);
        var editor = viewModel.CreateEditor();
        editor.Pattern = " .tmp ";
        editor.ExcludeFromHistory = true;

        viewModel.SaveRule(null, editor);
        var reloaded = new FileSyncFilterSettingViewModel(new ConfigBase(_configPath));
        var saved = reloaded.Rules.Single();
        Assert.AreEqual(".tmp", saved.Pattern);
        Assert.IsTrue(saved.ExcludeFromHistory);

        editor = reloaded.CreateEditor(saved);
        Assert.IsTrue(editor.ExcludeFromHistory);
        editor.ExcludeFromHistory = false;
        reloaded.SaveRule(saved, editor);

        var config = new ConfigBase(_configPath).GetConfig<FileFilterConfig>();
        Assert.HasCount(1, config.BlackList);
        Assert.IsFalse(config.BlackList.Single().ExcludeFromHistory);
    }

    [TestMethod]
    public void AddItem_DoesNotDuplicatePatternWithDifferentHistoryExclusion()
    {
        _config.SetConfig(new FileFilterConfig { FileFilterMode = "BlackList" });
        var viewModel = new FileSyncFilterSettingViewModel(_config);
        viewModel.SaveRule(null, new FileFilterRuleEditor { Pattern = ".tmp", CanExcludeFromHistory = true });
        viewModel.SaveRule(null, new FileFilterRuleEditor { Pattern = ".tmp", CanExcludeFromHistory = true, ExcludeFromHistory = true });

        Assert.HasCount(1, viewModel.Rules);
        Assert.HasCount(1, _config.GetConfig<FileFilterConfig>().BlackList);
    }

    [TestMethod]
    public void AddItem_DoesNotAddDuplicateRule()
    {
        _config.SetConfig(new FileFilterConfig { FileFilterMode = "BlackList" });
        var viewModel = new FileSyncFilterSettingViewModel(_config);
        var rule = viewModel.CreateEditor();
        rule.Pattern = ".tmp";

        viewModel.SaveRule(null, rule);
        viewModel.SaveRule(null, rule);

        Assert.HasCount(1, viewModel.Rules);
        Assert.HasCount(1, _config.GetConfig<FileFilterConfig>().BlackList);
    }

    [TestMethod]
    public void UpdateItem_DoesNotCreateDuplicateRule()
    {
        _config.SetConfig(new FileFilterConfig { FileFilterMode = "BlackList" });
        var viewModel = new FileSyncFilterSettingViewModel(_config);
        viewModel.SaveRule(null, new FileFilterRuleEditor { Pattern = ".tmp", CanExcludeFromHistory = true });
        viewModel.SaveRule(null, new FileFilterRuleEditor { Pattern = ".log", CanExcludeFromHistory = true });
        var itemToUpdate = viewModel.Rules[1];

        var edit = viewModel.CreateEditor(itemToUpdate);
        edit.Pattern = ".tmp";
        viewModel.SaveRule(itemToUpdate, edit);

        Assert.AreEqual(".log", itemToUpdate.Pattern);
        CollectionAssert.AreEqual(
            ExpectedDistinctPatterns,
            _config.GetConfig<FileFilterConfig>().BlackList.Select(rule => rule.Pattern).ToArray());
    }

    [TestMethod]
    public void SavingRule_ReconcilesPreexistingDuplicateRules()
    {
        var duplicateRule = new FileFilterRule { Pattern = ".tmp" };
        _config.SetConfig(new FileFilterConfig
        {
            FileFilterMode = "BlackList",
            BlackList = [duplicateRule, duplicateRule with { }],
        });
        var viewModel = new FileSyncFilterSettingViewModel(_config);

        var addition = viewModel.CreateEditor();
        addition.Pattern = ".log";
        viewModel.SaveRule(null, addition);

        CollectionAssert.AreEqual(ExpectedDistinctPatterns, viewModel.Rules.Select(item => item.Pattern).ToArray());
        CollectionAssert.AreEqual(ExpectedDistinctPatterns, _config.GetConfig<FileFilterConfig>().BlackList.Select(rule => rule.Pattern).ToArray());
    }

    [TestMethod]
    public void RuleEditor_PreservesRegexWhitespaceAndTrimsSuffixWhitespace()
    {
        const string regexPattern = @" ^report\.txt$ ";
        var regexRule = new FileFilterRule
        {
            Pattern = regexPattern,
            MatchMode = FileFilterMatchMode.Regex,
        };

        Assert.AreEqual(regexPattern, new FileFilterRuleEditor(regexRule).ToRule().Pattern);
        Assert.AreEqual(
            ".tmp",
            new FileFilterRuleEditor(new FileFilterRule { Pattern = " .tmp " }).ToRule().Pattern);
    }
}
