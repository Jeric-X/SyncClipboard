using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record class ClipboardAssistConfig
{
    public const string ConfigKey = "ClipboardAssist";

    public bool EasyCopyImageSwitchOn { get; set; } = false;
    public bool DownloadWebImage { get; set; } = false;
    public bool ConvertSwitchOn { get; set; } = false;
}
