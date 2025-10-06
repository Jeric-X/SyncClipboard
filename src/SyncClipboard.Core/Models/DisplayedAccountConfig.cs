namespace SyncClipboard.Core.Models;

public record DisplayedAccountConfig
{
    public string AccountId { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}