using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public partial class FileSyncFilterSettingViewModel : ObservableObject
{
    public static readonly LocaleString<string>[] Modes =
    [
        new("", Strings.None),
        new("BlackList", Strings.BlackList),
        new("WhiteList", Strings.WhiteList),
    ];

    public static readonly LocaleString<FileFilterMatchMode>[] MatchModes =
    [
        new(FileFilterMatchMode.Suffix, Strings.SuffixMatch),
        new(FileFilterMatchMode.Regex, Strings.RegularExpression),
    ];

    [ObservableProperty]
    private LocaleString<string> filterMode = Modes[0];

    partial void OnFilterModeChanged(LocaleString<string> value)
    {
        if (_isUpdating)
        {
            return;
        }

        FilterConfig = FilterConfig with { FileFilterMode = value.Key };
    }

    [ObservableProperty]
    private FileFilterConfig filterConfig = new();

    partial void OnFilterConfigChanged(FileFilterConfig value)
    {
        if (_isSavingList)
        {
            return;
        }

        _isUpdating = true;
        FilterMode = Modes.FirstOrDefault(mode => mode.Key == value.FileFilterMode) ?? Modes[0];
        UpdateFilterList();
        _isUpdating = false;
        _configManager.SetConfig(value);
    }

    [ObservableProperty]
    private bool enableList;

    public ObservableCollection<EditableFileFilterRule> FilterList { get; } = [];

    private bool _isUpdating;
    private bool _isSavingList;
    private readonly ConfigBase _configManager;

    public FileSyncFilterSettingViewModel(ConfigManager configManager)
        : this((ConfigBase)configManager)
    {
    }

    internal FileSyncFilterSettingViewModel(ConfigBase configManager)
    {
        _configManager = configManager;
        configManager.GetAndListenConfig<FileFilterConfig>(config => FilterConfig = config);
    }

    public static FileFilterRuleEditor CreateRuleEditor(FileFilterRule? rule = null) => new(rule);

    public static string? ValidateRuleEditor(FileFilterRuleEditor editor)
    {
        if (string.IsNullOrWhiteSpace(editor.Pattern))
        {
            return Strings.FileFilterPatternRequired;
        }

        var rule = editor.ToRule();
        if (!FileFilterHelper.TryValidateRule(rule, out var error))
        {
            return string.Format(Strings.InvalidRegularExpression, error);
        }

        return null;
    }

    public void AddItem(FileFilterRule rule)
    {
        rule = NormalizeRule(rule);
        if (FilterConfig.FileFilterMode == ""
            || !FileFilterHelper.TryValidateRule(rule, out _)
            || ContainsRule(rule))
        {
            return;
        }

        FilterList.Add(new EditableFileFilterRule(rule));
        SaveToConfig();
    }

    public void UpdateItem(EditableFileFilterRule item, FileFilterRule rule)
    {
        rule = NormalizeRule(rule);
        if (!FileFilterHelper.TryValidateRule(rule, out _) || ContainsRule(rule, item))
        {
            return;
        }

        item.Pattern = rule.Pattern;
        item.MatchMode = LocaleString<FileFilterMatchMode>.Match(MatchModes, rule.MatchMode);
        SaveToConfig();
    }

    public void RemoveItem(EditableFileFilterRule item)
    {
        FilterList.Remove(item);
        SaveToConfig();
    }

    public void SaveToConfig()
    {
        if (_isUpdating)
        {
            return;
        }

        var list = FilterList
            .Where(item => !string.IsNullOrWhiteSpace(item.Pattern))
            .Select(item => item.ToRule())
            .Distinct()
            .ToList();

        SynchronizeFilterList(list);

        if (FilterConfig.FileFilterMode == "BlackList")
        {
            SaveFilterConfig(FilterConfig with { BlackList = list });
        }
        else if (FilterConfig.FileFilterMode == "WhiteList")
        {
            SaveFilterConfig(FilterConfig with { WhiteList = list });
        }
    }

    [RelayCommand]
    public void Confirm()
    {
        SaveToConfig();
        AppCore.Current.Services.GetRequiredService<MainViewModel>().NavigateToLastLevel();
    }

    private void UpdateFilterList()
    {
        FilterList.Clear();
        var list = FilterConfig.FileFilterMode switch
        {
            "BlackList" => FilterConfig.BlackList,
            "WhiteList" => FilterConfig.WhiteList,
            _ => [],
        };

        foreach (var rule in list)
        {
            FilterList.Add(new EditableFileFilterRule(rule));
        }

        EnableList = FilterConfig.FileFilterMode != "";
    }

    private void SaveFilterConfig(FileFilterConfig value)
    {
        _isSavingList = true;
        try
        {
            FilterConfig = value;
        }
        finally
        {
            _isSavingList = false;
        }

        _configManager.SetConfig(value);
    }

    private bool ContainsRule(FileFilterRule rule, EditableFileFilterRule? itemToIgnore = null) => FilterList.Any(
        item => !ReferenceEquals(item, itemToIgnore) && item.ToRule() == rule);

    private void SynchronizeFilterList(IReadOnlyList<FileFilterRule> rules)
    {
        if (FilterList.Select(item => item.ToRule()).SequenceEqual(rules))
        {
            return;
        }

        FilterList.Clear();
        foreach (var rule in rules)
        {
            FilterList.Add(new EditableFileFilterRule(rule));
        }
    }

    private static FileFilterRule NormalizeRule(FileFilterRule rule) => new()
    {
        Pattern = rule.MatchMode == FileFilterMatchMode.Regex ? rule.Pattern : rule.Pattern.Trim(),
        MatchMode = rule.MatchMode,
    };
}

public partial class EditableFileFilterRule : ObservableObject
{
    [ObservableProperty]
    private string pattern = "";

    [ObservableProperty]
    private LocaleString<FileFilterMatchMode> matchMode = FileSyncFilterSettingViewModel.MatchModes[0];

    public EditableFileFilterRule(FileFilterRule rule)
    {
        Pattern = rule.Pattern;
        MatchMode = LocaleString<FileFilterMatchMode>.Match(FileSyncFilterSettingViewModel.MatchModes, rule.MatchMode);
    }

    public FileFilterRule ToRule() => new()
    {
        Pattern = MatchMode.Key == FileFilterMatchMode.Regex ? Pattern : Pattern.Trim(),
        MatchMode = MatchMode.Key,
    };
}

public partial class FileFilterRuleEditor : ObservableObject
{
    [ObservableProperty]
    private string pattern = "";

    [ObservableProperty]
    private LocaleString<FileFilterMatchMode> matchMode = FileSyncFilterSettingViewModel.MatchModes[0];

    public FileFilterRuleEditor(FileFilterRule? rule = null)
    {
        if (rule is null)
        {
            return;
        }

        Pattern = rule.Pattern;
        MatchMode = LocaleString<FileFilterMatchMode>.Match(FileSyncFilterSettingViewModel.MatchModes, rule.MatchMode);
    }

    public FileFilterRule ToRule() => new()
    {
        Pattern = MatchMode.Key == FileFilterMatchMode.Regex ? Pattern : Pattern.Trim(),
        MatchMode = MatchMode.Key,
    };
}
