using SyncClipboard.Core.Commons;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Shared.Models;

namespace SyncClipboard.Test;

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
        var editor = FileSyncFilterSettingViewModel.CreateRuleEditor();
        editor.Pattern = " .tmp ";
        editor.ExcludeFromHistory = true;

        viewModel.AddItem(editor.ToRule());
        var reloaded = new FileSyncFilterSettingViewModel(new ConfigBase(_configPath));
        var saved = reloaded.FilterList.Single();
        Assert.AreEqual(".tmp", saved.Pattern);
        Assert.IsTrue(saved.ExcludeFromHistory);

        editor = FileSyncFilterSettingViewModel.CreateRuleEditor(saved.ToRule());
        Assert.IsTrue(editor.ExcludeFromHistory);
        editor.ExcludeFromHistory = false;
        reloaded.UpdateItem(saved, editor.ToRule());

        var config = new ConfigBase(_configPath).GetConfig<FileFilterConfig>();
        Assert.HasCount(1, config.BlackList);
        Assert.IsFalse(config.BlackList.Single().ExcludeFromHistory);
    }

    [TestMethod]
    public void AddItem_DoesNotDuplicatePatternWithDifferentHistoryExclusion()
    {
        _config.SetConfig(new FileFilterConfig { FileFilterMode = "BlackList" });
        var viewModel = new FileSyncFilterSettingViewModel(_config);
        viewModel.AddItem(new FileFilterRule { Pattern = ".tmp" });
        viewModel.AddItem(new FileFilterRule { Pattern = ".tmp", ExcludeFromHistory = true });

        Assert.HasCount(1, viewModel.FilterList);
        Assert.HasCount(1, _config.GetConfig<FileFilterConfig>().BlackList);
    }

    [TestMethod]
    public void AddItem_DoesNotAddDuplicateRule()
    {
        _config.SetConfig(new FileFilterConfig { FileFilterMode = "BlackList" });
        var viewModel = new FileSyncFilterSettingViewModel(_config);
        var rule = new FileFilterRule { Pattern = ".tmp" };

        viewModel.AddItem(rule);
        viewModel.AddItem(rule);

        Assert.HasCount(1, viewModel.FilterList);
        Assert.HasCount(1, _config.GetConfig<FileFilterConfig>().BlackList);
    }

    [TestMethod]
    public void UpdateItem_DoesNotCreateDuplicateRule()
    {
        _config.SetConfig(new FileFilterConfig { FileFilterMode = "BlackList" });
        var viewModel = new FileSyncFilterSettingViewModel(_config);
        viewModel.AddItem(new FileFilterRule { Pattern = ".tmp" });
        viewModel.AddItem(new FileFilterRule { Pattern = ".log" });
        var itemToUpdate = viewModel.FilterList[1];

        viewModel.UpdateItem(itemToUpdate, new FileFilterRule { Pattern = ".tmp" });

        Assert.AreEqual(".log", itemToUpdate.Pattern);
        CollectionAssert.AreEqual(
            ExpectedDistinctPatterns,
            _config.GetConfig<FileFilterConfig>().BlackList.Select(rule => rule.Pattern).ToArray());
    }

    [TestMethod]
    public void SaveToConfig_ReconcilesPreexistingDuplicateRules()
    {
        var duplicateRule = new FileFilterRule { Pattern = ".tmp" };
        _config.SetConfig(new FileFilterConfig
        {
            FileFilterMode = "BlackList",
            BlackList = [duplicateRule, duplicateRule with { }],
        });
        var viewModel = new FileSyncFilterSettingViewModel(_config);

        viewModel.SaveToConfig();

        Assert.HasCount(1, viewModel.FilterList);
        Assert.HasCount(1, _config.GetConfig<FileFilterConfig>().BlackList);
    }
}
