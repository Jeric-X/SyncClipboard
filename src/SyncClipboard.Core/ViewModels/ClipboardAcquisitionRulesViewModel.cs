using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.ViewModels;

public partial class ClipboardAcquisitionRulesViewModel : ObservableObject
{
    public static readonly LocaleString<TextImageRule>[] TextImageRules =
    [
        new (TextImageRule.Text, Strings.AcquireText),
        new (TextImageRule.Image, Strings.AcquireImage)
    ];

    [ObservableProperty]
    private LocaleString<TextImageRule> textImageRuleSelection = LocaleString<TextImageRule>.Match(TextImageRules, TextImageRule.Text);
    partial void OnTextImageRuleSelectionChanged(LocaleString<TextImageRule> value) =>
        AcquisitionConfig = AcquisitionConfig with { TextImageRule = value.Key };

    [ObservableProperty]
    private ClipboardAcquisitionConfig acquisitionConfig = new();
    partial void OnAcquisitionConfigChanged(ClipboardAcquisitionConfig value)
    {
        TextImageRuleSelection = LocaleString<TextImageRule>.Match(TextImageRules, value.TextImageRule);
        _configManager.SetConfig(value);
    }

    private readonly ConfigManager _configManager;

    public ClipboardAcquisitionRulesViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        configManager.GetAndListenConfig<ClipboardAcquisitionConfig>(config => AcquisitionConfig = config);
    }
}
