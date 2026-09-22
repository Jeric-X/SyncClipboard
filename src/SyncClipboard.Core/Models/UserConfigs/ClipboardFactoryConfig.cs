using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record class ClipboardFactoryConfig
{
    public const string ConfigKey = "ClipboardFactory";

    public ClipboardReadMethod ReadMethod { get; set; } = ClipboardReadMethod.Avalonia;

    public ClipboardWriteMethod WriteMethod { get; set; } = ClipboardWriteMethod.Avalonia;
}
