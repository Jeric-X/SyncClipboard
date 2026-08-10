using System.Net;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Models;

public enum WifiAccessStatus
{
    Available,
    NotRequested,
    Denied,
    Unsupported,
    Error,
}

public record WifiNetworkInfo(string InterfaceId, string InterfaceName, string Ssid);

public record NetworkInterfaceSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool HasDefaultGateway { get; init; }
    public IReadOnlyList<IPAddress> Addresses { get; init; } = [];
    public string? WifiSsid { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Description) || Description == Name
        ? Name
        : $"{Name} ({Description})";
}

public record NetworkContextSnapshot
{
    public IReadOnlyList<NetworkInterfaceSnapshot> Interfaces { get; init; } = [];
    public WifiAccessStatus WifiStatus { get; init; } = WifiAccessStatus.Unsupported;
    public string? WifiError { get; init; }

    public string Fingerprint => string.Join("|", Interfaces
        .OrderBy(item => item.Id, StringComparer.Ordinal)
        .Select(item => $"{item.Id}:{item.Name}:{item.HasDefaultGateway}:{item.WifiSsid}:{string.Join(',', item.Addresses.OrderBy(address => address.ToString()).Select(address => address.ToString()))}"))
        + $"|wifi:{WifiStatus}";
}

public record NetworkRuleMatchResult(NetworkAccountSwitchRule Rule, NetworkInterfaceSnapshot Interface);
