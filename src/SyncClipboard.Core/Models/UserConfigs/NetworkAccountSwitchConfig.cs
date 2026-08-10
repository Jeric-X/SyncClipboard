namespace SyncClipboard.Core.Models.UserConfigs;

public enum NetworkRuleMatchMode
{
    Any,
    All,
}

public enum NetworkNoMatchAction
{
    KeepCurrent,
    SwitchToDefaultAccount,
    RemoveSyncAccount,
}

public record NetworkAccountSwitchRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public AccountConfig TargetAccount { get; set; } = new();
    public NetworkRuleMatchMode MatchMode { get; set; } = NetworkRuleMatchMode.Any;
    public string NetworkInterfaceId { get; set; } = string.Empty;
    public string NetworkInterfaceName { get; set; } = string.Empty;
    public List<string> WifiSsids { get; set; } = [];
    public List<string> IpRanges { get; set; } = [];

    public virtual bool Equals(NetworkAccountSwitchRule? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;

        return Id == other.Id
            && Name == other.Name
            && Enabled == other.Enabled
            && TargetAccount == other.TargetAccount
            && MatchMode == other.MatchMode
            && NetworkInterfaceId == other.NetworkInterfaceId
            && NetworkInterfaceName == other.NetworkInterfaceName
            && WifiSsids.SequenceEqual(other.WifiSsids, StringComparer.Ordinal)
            && IpRanges.SequenceEqual(other.IpRanges, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(Id, Name, Enabled, TargetAccount, MatchMode, NetworkInterfaceId, NetworkInterfaceName);
        foreach (var value in WifiSsids.Concat(IpRanges))
        {
            hash = HashCode.Combine(hash, value);
        }
        return hash;
    }
}

public record NetworkAccountSwitchConfig
{
    public bool Enabled { get; set; }
    public bool NotifyOnChange { get; set; } = true;
    public bool WifiAccessRequested { get; set; }
    public NetworkNoMatchAction NoMatchAction { get; set; } = NetworkNoMatchAction.KeepCurrent;
    public AccountConfig DefaultAccount { get; set; } = new();
    public List<NetworkAccountSwitchRule> Rules { get; set; } = [];

    public virtual bool Equals(NetworkAccountSwitchConfig? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;

        return Enabled == other.Enabled
            && NotifyOnChange == other.NotifyOnChange
            && WifiAccessRequested == other.WifiAccessRequested
            && NoMatchAction == other.NoMatchAction
            && DefaultAccount == other.DefaultAccount
            && Rules.SequenceEqual(other.Rules);
    }

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(Enabled, NotifyOnChange, WifiAccessRequested, NoMatchAction, DefaultAccount);
        foreach (var rule in Rules)
        {
            hash = HashCode.Combine(hash, rule);
        }
        return hash;
    }
}
