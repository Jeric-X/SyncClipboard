using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.ViewModels;

public sealed class FileSyncFilterSettingViewModel : SyncFilterSettingViewModel<FileFilterConfig, FileFilterRule>
{
    public FileSyncFilterSettingViewModel(ConfigManager configManager) : this((ConfigBase)configManager) { }
    internal FileSyncFilterSettingViewModel(ConfigBase configManager) : base(configManager) => Load();

    protected override string GetMode(FileFilterConfig config) => config.FileFilterMode;
    protected override FileFilterConfig WithMode(FileFilterConfig config, string mode) => config with { FileFilterMode = mode };
    protected override List<FileFilterRule> GetRules(FileFilterConfig config, bool blackList) => blackList ? config.BlackList : config.WhiteList;
    protected override FileFilterRuleEditor NewEditor(FileFilterRule? rule, bool blackList) => new(rule) { CanExcludeFromHistory = blackList };

    protected override bool CanSaveRule(List<FileFilterRule> rules, int index, FileFilterRule rule) =>
        !rules.Where((_, i) => i != index)
            .Any(existing => NormalizePattern(existing) == rule.Pattern && existing.MatchMode == rule.MatchMode);

    protected override void NormalizeRules(List<FileFilterRule> rules)
    {
        var seen = new HashSet<(string, FileFilterMatchMode)>();
        rules.RemoveAll(rule => string.IsNullOrWhiteSpace(rule.Pattern) || !seen.Add((NormalizePattern(rule), rule.MatchMode)));
        foreach (var rule in rules)
            rule.Pattern = NormalizePattern(rule);
    }

    private static string NormalizePattern(FileFilterRule rule) =>
        rule.MatchMode == FileFilterMatchMode.Regex ? rule.Pattern : rule.Pattern.Trim();
}

public class FileFilterRuleEditor : FilterRuleEditor<FileFilterRule>
{
    private static readonly LocaleString<bool>[] FileMatchModes = CreateMatchModes(Strings.SuffixMatch);
    public override IReadOnlyList<LocaleString<bool>> MatchModes => FileMatchModes;
    public override string? ValidationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Pattern))
                return Strings.FileFilterPatternRequired;
            return FileFilterHelper.TryValidateRule(ToRule(), out var error)
                ? null : string.Format(Strings.InvalidRegularExpression, error);
        }
    }

    public FileFilterRuleEditor(FileFilterRule? rule = null)
    {
        if (rule is null)
            return;

        Pattern = rule.Pattern;
        UseRegex = rule.MatchMode == FileFilterMatchMode.Regex;
        ExcludeFromHistory = rule.ExcludeFromHistory;
    }

    public override FileFilterRule ToRule() => new()
    {
        Pattern = UseRegex ? Pattern : Pattern.Trim(),
        MatchMode = UseRegex ? FileFilterMatchMode.Regex : FileFilterMatchMode.Suffix,
        ExcludeFromHistory = ExcludeFromHistory,
    };
}
