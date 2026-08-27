using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record class ClipboardAcquisitionConfig
{
    public const string ConfigKey = "ClipboardAcquisition";

    public TextImageRule TextImageRule { get; set; } = TextImageRule.Text;
}
