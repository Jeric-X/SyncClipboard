using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities.Network;

public static class NetworkAccountSwitchStatusFormatter
{
    public static string Format(NetworkAccountSwitchStatus status) => status.State switch
    {
        NetworkAccountSwitchState.Disabled => Strings.Disabled,
        NetworkAccountSwitchState.Evaluating => Strings.Evaluating,
        NetworkAccountSwitchState.Matched => string.Format(Strings.Matched, status.RuleName, status.AccountName),
        NetworkAccountSwitchState.NoMatch => string.IsNullOrEmpty(status.AccountName)
            ? Strings.NoMatch
            : string.Format(Strings.NoMatchSwitchDefault, status.AccountName),
        NetworkAccountSwitchState.AccountRemoved => !string.IsNullOrEmpty(status.Detail) ? status.Detail : Strings.SyncAccountRemoved,
        NetworkAccountSwitchState.ManualOverride => Strings.ManualOverride,
        NetworkAccountSwitchState.Error => status.Error ?? NetworkAccountSwitchState.Error.ToString(),
        _ => status.State.ToString(),
    };
}
