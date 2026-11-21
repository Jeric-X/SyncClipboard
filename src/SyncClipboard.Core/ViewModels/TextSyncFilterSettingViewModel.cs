using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public sealed class TextSyncFilterSettingViewModel : SyncFilterSettingViewModel<TextFilterConfig, TextFilterRule>
{
    public TextSyncFilterSettingViewModel(ConfigManager configManager) : base(configManager) => Load();

    protected override string GetMode(TextFilterConfig config) => config.FilterMode;
    protected override TextFilterConfig WithMode(TextFilterConfig config, string mode) => config with { FilterMode = mode };
    protected override List<TextFilterRule> GetRules(TextFilterConfig config, bool blackList) => blackList ? config.BlackList : config.WhiteList;
    protected override TextFilterRuleEditor NewEditor(TextFilterRule? rule, bool blackList) => new(rule ?? new(""), blackList);
}

public class TextFilterRuleEditor : FilterRuleEditor<TextFilterRule>
{
    private static readonly LocaleString<bool>[] TextMatchModes = CreateMatchModes(Strings.KeywordMatch);
    public override IReadOnlyList<LocaleString<bool>> MatchModes => TextMatchModes;
    public override string? ValidationError => TextFilterHelper.ValidateRule(ToRule());

    public TextFilterRuleEditor(TextFilterRule rule, bool canExcludeFromHistory)
    {
        Pattern = rule.Pattern;
        UseRegex = rule.UseRegex;
        CanExcludeFromHistory = canExcludeFromHistory;
        ExcludeFromHistory = canExcludeFromHistory && rule.ExcludeFromHistory;
    }

    public override TextFilterRule ToRule() => new(Pattern, UseRegex, CanExcludeFromHistory && ExcludeFromHistory);
}
