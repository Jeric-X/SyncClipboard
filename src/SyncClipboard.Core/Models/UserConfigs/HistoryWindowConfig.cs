using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.Runtime)]
public record class HistoryWindowConfig
{
    public const string ConfigKey = "HistoryWindow";

    public int Width { get; set; } = 850;
    public int Height { get; set; } = 530;
    public bool IsTopmost { get; set; } = false;
    public bool ScrollToTopOnReopen { get; set; } = false;
    public bool CloseWhenLostFocus { get; set; } = true;
    public bool ShowSyncState { get; set; } = true;
    public bool OnlyShowStarred { get; set; } = false;
    public bool ShowStarredFilter { get; set; } = false;
    public bool SortByLastAccessed { get; set; } = false;
    public int FontScalePercent { get; set; } = 100;
    public bool FollowCaretPosition { get; set; } = false;
    public bool FollowForegroundWindowScreen { get; set; } = false;
    public bool FollowMousePosition { get; set; } = false;
    public bool ShowPreviewPanel { get; set; } = false;
    public int ListViewWidth { get; set; } = 550;
    public bool CompactListWhenPreview { get; set; } = true;
    public int PasteDelayAfterWindowHiddenMilliseconds { get; set; } = 200;
}
