using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public partial class TextSyncFilterSettingViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;
    private readonly ObservableCollection<EditableTextFilterRule> _blackList;
    private readonly ObservableCollection<EditableTextFilterRule> _whiteList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rules), nameof(Enabled))]
    private LocaleString<string> filterMode;

    [ObservableProperty]
    private string? error;

    public bool Enabled => FilterMode.Key != "";
    public ObservableCollection<EditableTextFilterRule> Rules => FilterMode.Key == "WhiteList" ? _whiteList : _blackList;

    public TextSyncFilterSettingViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        var config = configManager.GetConfig<TextFilterConfig>();
        _blackList = new(config.BlackList.Select(rule => new EditableTextFilterRule(rule, true)));
        _whiteList = new(config.WhiteList.Select(rule => new EditableTextFilterRule(rule, false)));
        filterMode = LocaleString<string>.Match(FileSyncFilterSettingViewModel.Modes, config.FilterMode);
    }

    [RelayCommand]
    private void AddRule() => Rules.Add(new(new(""), FilterMode.Key == "BlackList"));

    [RelayCommand]
    private void Confirm()
    {
        var config = new TextFilterConfig
        {
            FilterMode = FilterMode.Key,
            BlackList = _blackList.Select(item => item.ToRule()).ToList(),
            WhiteList = _whiteList.Select(item => item.ToRule()).ToList(),
        };

        foreach (var (mode, rules) in new[] { ("BlackList", config.BlackList), ("WhiteList", config.WhiteList) })
        {
            foreach (var rule in rules)
            {
                Error = TextFilterHelper.ValidateRule(rule);
                if (Error is not null)
                {
                    FilterMode = LocaleString<string>.Match(FileSyncFilterSettingViewModel.Modes, mode);
                    return;
                }
            }
        }

        _configManager.SetConfig(config);
        AppCore.Current.Services.GetRequiredService<MainViewModel>().NavigateToLastLevel();
    }
}

public partial class EditableTextFilterRule(TextFilterRule rule, bool canExcludeFromHistory) : ObservableObject
{
    public bool CanExcludeFromHistory { get; } = canExcludeFromHistory;
    [ObservableProperty] private string pattern = rule.Pattern;
    [ObservableProperty] private bool useRegex = rule.UseRegex;
    [ObservableProperty] private bool excludeFromHistory = rule.ExcludeFromHistory;

    public TextFilterRule ToRule() => new(Pattern, UseRegex, CanExcludeFromHistory && ExcludeFromHistory);
}
