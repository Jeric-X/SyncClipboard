using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public partial class TextSyncFilterSettingViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;
    private TextFilterConfig _config = new();

    public LocaleString<string> FilterMode
    {
        get => LocaleString<string>.Match(FileSyncFilterSettingViewModel.Modes, _config.FilterMode);
        set
        {
            if (value is not null && value.Key != _config.FilterMode)
                _configManager.SetConfig(_config with { FilterMode = value.Key });
        }
    }

    public bool Enabled => FilterMode.Key != "";
    public ReadOnlyCollection<EditableTextFilterRule> Rules { get; private set; } = new([]);

    public TextSyncFilterSettingViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        configManager.ConfigChanged += Refresh;
        Refresh();
    }

    public EditableTextFilterRule CreateEditor(EditableTextFilterRule? item) =>
        new(item?.ToRule() ?? new TextFilterRule(""), FilterMode.Key == "BlackList");

    public void SaveRule(EditableTextFilterRule? item, EditableTextFilterRule editor)
    {
        if (!editor.IsValid)
            return;

        var config = _configManager.GetConfig<TextFilterConfig>();
        var rules = editor.CanExcludeFromHistory ? config.BlackList : config.WhiteList;
        if (item is null)
            rules.Add(editor.ToRule());
        else
        {
            var index = Rules.IndexOf(item);
            if (index < 0 || index >= rules.Count)
                return;
            rules[index] = editor.ToRule();
        }
        _configManager.SetConfig(config);
    }

    public void DeleteRule(EditableTextFilterRule item)
    {
        var config = _configManager.GetConfig<TextFilterConfig>();
        var rules = item.CanExcludeFromHistory ? config.BlackList : config.WhiteList;
        var index = Rules.IndexOf(item);
        if (index < 0 || index >= rules.Count)
            return;
        rules.RemoveAt(index);
        _configManager.SetConfig(config);
    }

    private void Refresh()
    {
        var config = _configManager.GetConfig<TextFilterConfig>();
        if (!SetProperty(ref _config, config, nameof(FilterMode)))
            return;
        var blackList = FilterMode.Key == "BlackList";
        var rules = blackList ? config.BlackList : config.WhiteList;
        Rules = Enabled ? rules.Select(rule => new EditableTextFilterRule(rule, blackList)).ToList().AsReadOnly() : new([]);
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Rules));
    }
}

public class EditableTextFilterRule : FilterRuleEditor
{
    private static readonly LocaleString<bool>[] TextMatchModes = CreateMatchModes(Strings.KeywordMatch);
    public override IReadOnlyList<LocaleString<bool>> MatchModes => TextMatchModes;
    public override string? ValidationError => TextFilterHelper.ValidateRule(ToRule());

    public EditableTextFilterRule(TextFilterRule rule, bool canExcludeFromHistory)
    {
        Pattern = rule.Pattern;
        UseRegex = rule.UseRegex;
        CanExcludeFromHistory = canExcludeFromHistory;
        ExcludeFromHistory = canExcludeFromHistory && rule.ExcludeFromHistory;
    }

    public TextFilterRule ToRule() => new(Pattern, UseRegex, CanExcludeFromHistory && ExcludeFromHistory);
}
