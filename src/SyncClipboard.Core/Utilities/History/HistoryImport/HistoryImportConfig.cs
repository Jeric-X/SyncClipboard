using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Utilities.History.HistoryImport;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record class HistoryImportConfig
{
    public const string ConfigKey = "HistoryImport";

    public uint MaxGroupEntryCount { get; set; } = 1_000_000;
}
