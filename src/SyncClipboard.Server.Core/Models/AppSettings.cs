namespace SyncClipboard.Server.Core.Models;

public sealed class AppSettings
{
    public string UserName { get; set; } = "admin";
    public string Password { get; set; } = "admin";
    public uint MaxSavedHistoryCount { get; set; } = 1000;
    public uint HistoryRetentionMinutes { get; set; } = 10080;
}