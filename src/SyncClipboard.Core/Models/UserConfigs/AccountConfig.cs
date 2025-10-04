namespace SyncClipboard.Core.Models.UserConfigs;

public record AccountConfig
{
    public string AccountId { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
}
