using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Network;

public sealed record NetworkAccountSwitchCleanupResult(
    NetworkAccountSwitchConfig Config,
    int RemovedRuleCount,
    bool RemovedDefaultAccount);

public static class NetworkAccountSwitchConfigCleaner
{
    public static NetworkAccountSwitchCleanupResult RemoveAccount(
        NetworkAccountSwitchConfig config,
        AccountConfig account)
    {
        var rules = config.Rules.Where(rule => rule.TargetAccount != account).ToList();
        var removedDefault = config.DefaultAccount == account;
        var updated = config with
        {
            Rules = rules,
            DefaultAccount = removedDefault ? new() : config.DefaultAccount,
            NoMatchAction = removedDefault && config.NoMatchAction == NetworkNoMatchAction.SwitchToDefaultAccount
                ? NetworkNoMatchAction.RemoveSyncAccount
                : config.NoMatchAction,
        };

        return new(updated, config.Rules.Count - rules.Count, removedDefault);
    }
}
