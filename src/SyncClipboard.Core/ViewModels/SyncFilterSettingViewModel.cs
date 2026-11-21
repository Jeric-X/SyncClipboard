using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public abstract class SyncFilterSettingViewModel : ObservableObject
{
    public static readonly LocaleString<string>[] Modes =
    [
        new("", Strings.None),
        new("BlackList", Strings.BlackList),
        new("WhiteList", Strings.WhiteList),
    ];

    public abstract LocaleString<string> FilterMode { get; set; }
    public bool Enabled => FilterMode.Key != "";
    public ReadOnlyCollection<FilterRuleEditor> Rules { get; protected set; } = new([]);
    public abstract FilterRuleEditor CreateEditor(FilterRuleEditor? item = null);
    public abstract void SaveRule(FilterRuleEditor? item, FilterRuleEditor editor);
    public abstract void DeleteRule(FilterRuleEditor item);
}

public abstract class SyncFilterSettingViewModel<TConfig, TRule>(ConfigBase configManager) : SyncFilterSettingViewModel
    where TConfig : class, IEquatable<TConfig>, new()
    where TRule : class
{
    private static readonly TConfig DefaultConfig = new();
    private TConfig _config = DefaultConfig;

    public override LocaleString<string> FilterMode
    {
        get => LocaleString<string>.Match(Modes, GetMode(_config));
        set
        {
            if (value is not null && value.Key != GetMode(_config))
                configManager.SetConfig(WithMode(_config, value.Key));
        }
    }

    protected abstract string GetMode(TConfig config);
    protected abstract TConfig WithMode(TConfig config, string mode);
    protected abstract List<TRule> GetRules(TConfig config, bool blackList);
    protected abstract FilterRuleEditor<TRule> NewEditor(TRule? rule, bool blackList);

    protected void Load()
    {
        configManager.GetAndListenConfig<TConfig>(Refresh);
        var key = SyncClipboardConfigRegistry.GetDefaultKey<TConfig>();
        // Reload only notifies typed listeners for sections present in the new configuration.
        configManager.ConfigChanged += () =>
        {
            if (configManager.GetNode(key) is null)
                Refresh(DefaultConfig);
        };
    }

    private void Refresh(TConfig config)
    {
        if (!SetProperty(ref _config, config, nameof(FilterMode)))
            return;

        var blackList = FilterMode.Key == "BlackList";
        Rules = Enabled ? new([.. GetRules(config, blackList).Select(rule => NewEditor(rule, blackList))]) : new([]);
        OnPropertyChanged(nameof(Rules));
        OnPropertyChanged(nameof(Enabled));
    }

    public override FilterRuleEditor<TRule> CreateEditor(FilterRuleEditor? item = null) =>
        NewEditor((item as FilterRuleEditor<TRule>)?.ToRule(), FilterMode.Key == "BlackList");

    public override void SaveRule(FilterRuleEditor? item, FilterRuleEditor editor)
    {
        if (Enabled && editor is FilterRuleEditor<TRule> draft && draft.IsValid)
            ChangeRule(item, draft.ToRule(), draft.CanExcludeFromHistory);
    }

    public override void DeleteRule(FilterRuleEditor item) => ChangeRule(item, null, item.CanExcludeFromHistory);

    private void ChangeRule(FilterRuleEditor? item, TRule? replacement, bool blackList)
    {
        var config = configManager.GetConfig<TConfig>();
        var rules = GetRules(config, blackList);
        var index = item is null ? -1 : Rules.IndexOf(item);
        if (item is not null && (index < 0 || index >= rules.Count))
            return;

        if (replacement is null)
            rules.RemoveAt(index);
        else if (!CanSaveRule(rules, index, replacement))
            return;
        else if (item is null)
            rules.Add(replacement);
        else
            rules[index] = replacement;

        NormalizeRules(rules);
        configManager.SetConfig(config);
    }

    protected virtual bool CanSaveRule(List<TRule> rules, int index, TRule rule) => true;
    protected virtual void NormalizeRules(List<TRule> rules) { }
}
