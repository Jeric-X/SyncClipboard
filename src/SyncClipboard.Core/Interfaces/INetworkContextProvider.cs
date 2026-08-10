using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IWifiNetworkInfoProvider
{
    event EventHandler? NetworkChanged;
    Task<(WifiAccessStatus Status, IReadOnlyList<WifiNetworkInfo> Networks, string? Error)> GetConnectedNetworksAsync(
        bool requestAccess,
        CancellationToken cancellationToken = default);
    bool CanOpenWifiSettings { get; }
    void OpenWifiSettings();
}

public interface INetworkContextProvider
{
    event EventHandler? NetworkChanged;
    Task<NetworkContextSnapshot> GetCurrentAsync(bool requestWifiAccess = false, CancellationToken cancellationToken = default);
    bool CanOpenWifiSettings { get; }
    void OpenWifiSettings();
}

