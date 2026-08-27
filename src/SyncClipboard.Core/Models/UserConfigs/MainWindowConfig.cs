using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.Runtime)]
public record class MainWindowConfig
{
    public const string ConfigKey = "MainWindow";

    public int Width { get; set; } = 850;
    public int Height { get; set; } = 530;
}
