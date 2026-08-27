using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record AccountConfig
{
    public const string ConfigKey = "Account";
    public const string SavedAccountsConfigKey = "SavedAccounts";

    public string AccountId { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;

    public bool IsEmpty() =>
        string.IsNullOrWhiteSpace(AccountId) || string.IsNullOrWhiteSpace(AccountType);
}
