using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Network;

public enum NetworkAccountSwitchDecisionKind
{
    KeepCurrent,
    SwitchAccount,
    RemoveSyncAccount,
}

public sealed record NetworkAccountSwitchDecision(
    NetworkAccountSwitchDecisionKind Kind,
    AccountConfig? TargetAccount = null,
    NetworkRuleMatchResult? Match = null);

public static class NetworkAccountSwitchEvaluator
{
    public static NetworkAccountSwitchDecision Evaluate(
        NetworkAccountSwitchConfig config,
        NetworkContextSnapshot snapshot,
        Func<AccountConfig, bool> accountExists)
    {
        var match = NetworkRuleMatcher.Match(config.Rules, snapshot);
        if (match is not null)
        {
            return accountExists(match.Rule.TargetAccount)
                ? new(NetworkAccountSwitchDecisionKind.SwitchAccount, match.Rule.TargetAccount, match)
                : new(NetworkAccountSwitchDecisionKind.RemoveSyncAccount, Match: match);
        }

        return config.NoMatchAction switch
        {
            NetworkNoMatchAction.SwitchToDefaultAccount when accountExists(config.DefaultAccount) =>
                new(NetworkAccountSwitchDecisionKind.SwitchAccount, config.DefaultAccount),
            NetworkNoMatchAction.SwitchToDefaultAccount =>
                new(NetworkAccountSwitchDecisionKind.RemoveSyncAccount),
            NetworkNoMatchAction.RemoveSyncAccount =>
                new(NetworkAccountSwitchDecisionKind.RemoveSyncAccount),
            _ => new(NetworkAccountSwitchDecisionKind.KeepCurrent),
        };
    }
}
