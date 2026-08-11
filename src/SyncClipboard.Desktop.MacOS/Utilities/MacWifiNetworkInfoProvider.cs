using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppKit;
using CoreLocation;
using CoreWlan;
using Foundation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.MacOS.Utilities;

internal sealed class MacWifiNetworkInfoProvider : IWifiNetworkInfoProvider
{
    private readonly CLLocationManager _locationManager = new();
    private readonly CWWiFiClient _wifiClient = CWWiFiClient.SharedWiFiClient;

    public event EventHandler? NetworkChanged
    {
        add { }
        remove { }
    }

    public bool CanOpenWifiSettings => true;

    public Task<(WifiAccessStatus Status, IReadOnlyList<WifiNetworkInfo> Networks, string? Error)> GetConnectedNetworksAsync(
        bool requestAccess,
        CancellationToken cancellationToken = default)
    {
        if (requestAccess)
        {
            _locationManager.RequestWhenInUseAuthorization();
        }

        try
        {
            var networks = (_wifiClient.Interfaces ?? [])
                .Where(item => !string.IsNullOrEmpty(item.InterfaceName) && !string.IsNullOrEmpty(item.Ssid))
                .Select(item => new WifiNetworkInfo(item.InterfaceName!, item.InterfaceName!, item.Ssid!))
                .ToList();

            var status = _locationManager.AuthorizationStatus switch
            {
                CLAuthorizationStatus.AuthorizedAlways => WifiAccessStatus.Available,
                CLAuthorizationStatus.Denied or CLAuthorizationStatus.Restricted
                    => WifiAccessStatus.Denied,
                _ => requestAccess ? WifiAccessStatus.Denied : WifiAccessStatus.NotRequested,
            };
            return Task.FromResult<(WifiAccessStatus, IReadOnlyList<WifiNetworkInfo>, string?)>((status, networks, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(WifiAccessStatus, IReadOnlyList<WifiNetworkInfo>, string?)>(
                (WifiAccessStatus.Error, [], ex.Message));
        }
    }

    public void OpenWifiSettings()
    {
        NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl("x-apple.systempreferences:com.apple.preference.security?Privacy_LocationServices"));
    }
}
