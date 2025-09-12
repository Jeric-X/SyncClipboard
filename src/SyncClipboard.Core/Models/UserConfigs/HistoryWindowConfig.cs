namespace SyncClipboard.Core.Models.UserConfigs;

public record class HistoryWindowConfig
{
    public int Width { get; set; } = 850;
    public int Height { get; set; } = 530;
    public bool IsTopmost { get; set; } = false;
}
