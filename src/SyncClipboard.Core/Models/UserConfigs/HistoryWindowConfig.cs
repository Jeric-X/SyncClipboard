namespace SyncClipboard.Core.Models.UserConfigs;

public record class HistoryWindowConfig
{
    public int Width { get; set; } = 850;
    public int Height { get; set; } = 530;
    public bool IsTopmost { get; set; } = false;
    public bool ScrollToTopOnReopen { get; set; } = false;
    public bool CloseWhenLostFocus { get; set; } = true;
    public bool ShowSyncState { get; set; } = true;
    public bool OnlyShowLocal { get; set; } = false;
    public bool SortByLastAccessed { get; set; } = false;
    public bool ShowDetail { get; set; } = false;
    public int FontScalePercent { get; set; } = 100;
    public bool FollowCaretPosition { get; set; } = false;
    public bool FollowForegroundWindowScreen { get; set; } = false;
    public bool FollowMousePosition { get; set; } = false;
}
