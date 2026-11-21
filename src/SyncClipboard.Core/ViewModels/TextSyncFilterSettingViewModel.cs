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
    private LocaleString<string> _filterMode = FileSyncFilterSettingViewModel.Modes[0];

    public LocaleString<string> FilterMode
    {
        get => _filterMode;
        set
        {
            if (value is not null && value != _filterMode)
                Save(_configManager.GetConfig<TextFilterConfig>() with { FilterMode = value.Key });
        }
    }

    public bool Enabled => FilterMode.Key != "";
    public ReadOnlyCollection<EditableTextFilterRule> Rules { get; private set; } = new([]);

    public TextSyncFilterSettingViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        Refresh(configManager.GetConfig<TextFilterConfig>());
    }

    public EditableTextFilterRule CreateEditor(EditableTextFilterRule? item) =>
        new(item?.ToRule() ?? new TextFilterRule(""), FilterMode.Key == "BlackList");

    public bool TrySaveRule(EditableTextFilterRule? item, EditableTextFilterRule editor) =>
        editor.IsValid && ChangeRule(item, editor.ToRule(), editor.CanExcludeFromHistory);

    public void DeleteRule(EditableTextFilterRule item)
    {
        if (!ChangeRule(item, null, item.CanExcludeFromHistory))
            AppCore.Current.NotificationManager.ShowText(Strings.TextSyncFilter, Strings.TextFilterListChanged);
    }

    private bool ChangeRule(EditableTextFilterRule? item, TextFilterRule? replacement, bool blackList)
    {
        var config = _configManager.GetConfig<TextFilterConfig>();
        var rules = blackList ? config.BlackList : config.WhiteList;
        if (item is null && replacement is not null)
        {
            rules.Add(replacement);
        }
        else if (item is not null)
        {
            var index = Rules.IndexOf(item);
            if (index < 0 || index >= rules.Count || rules[index] != item.OriginalRule)
            {
                Refresh(config);
                return false;
            }
            if (replacement is null)
                rules.RemoveAt(index);
            else
                rules[index] = replacement;
        }
        Save(config);
        return true;
    }

    private void Save(TextFilterConfig config)
    {
        _configManager.SetConfig(config);
        Refresh(_configManager.GetConfig<TextFilterConfig>());
    }

    private void Refresh(TextFilterConfig config)
    {
        _filterMode = LocaleString<string>.Match(FileSyncFilterSettingViewModel.Modes, config.FilterMode);
        var blackList = FilterMode.Key == "BlackList";
        var rules = blackList ? config.BlackList : config.WhiteList;
        Rules = Enabled ? rules.Select(rule => new EditableTextFilterRule(rule, blackList)).ToList().AsReadOnly() : new([]);
        OnPropertyChanged(nameof(FilterMode));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Rules));
    }
}

public partial class EditableTextFilterRule(TextFilterRule rule, bool canExcludeFromHistory) : ObservableObject
{
    internal TextFilterRule OriginalRule { get; } = rule;
    public bool CanExcludeFromHistory { get; } = canExcludeFromHistory;
    public static readonly LocaleString<bool>[] MatchModes = [new(false, Strings.KeywordMatch), new(true, Strings.RegularExpression)];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid))]
    private string pattern = rule.Pattern;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(MatchMode), nameof(ValidationError), nameof(IsValid))]
    private bool useRegex = rule.UseRegex;

    [ObservableProperty] private bool excludeFromHistory = canExcludeFromHistory && rule.ExcludeFromHistory;

    public LocaleString<bool> MatchMode
    {
        get => LocaleString<bool>.Match(MatchModes, UseRegex);
        set
        {
            if (value is not null)
                UseRegex = value.Key;
        }
    }

    public bool IsValid => ValidationError is null;
    public string? ValidationError => TextFilterHelper.ValidateRule(ToRule());

    public TextFilterRule ToRule() => new(Pattern, UseRegex, CanExcludeFromHistory && ExcludeFromHistory);
}
