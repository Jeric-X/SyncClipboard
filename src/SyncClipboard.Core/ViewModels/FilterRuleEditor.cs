using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.ViewModels;

public abstract partial class FilterRuleEditor : ObservableObject
{
    public abstract IReadOnlyList<LocaleString<bool>> MatchModes { get; }
    public abstract string? ValidationError { get; }
    public bool IsValid => ValidationError is null;
    public bool CanExcludeFromHistory { get; init; }
    public bool ShowHistoryExclusion => CanExcludeFromHistory && ExcludeFromHistory;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ValidationError), nameof(IsValid))]
    private string pattern = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(MatchMode), nameof(ValidationError), nameof(IsValid))]
    private bool useRegex;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowHistoryExclusion))]
    private bool excludeFromHistory;

    public LocaleString<bool> MatchMode
    {
        get => LocaleString<bool>.Match(MatchModes, UseRegex);
        set
        {
            if (value is not null)
                UseRegex = value.Key;
        }
    }

    protected static LocaleString<bool>[] CreateMatchModes(string literalMatchLabel) =>
        [new(false, literalMatchLabel), new(true, Strings.RegularExpression)];
}

public abstract class FilterRuleEditor<TRule> : FilterRuleEditor
{
    public abstract TRule ToRule();
}
