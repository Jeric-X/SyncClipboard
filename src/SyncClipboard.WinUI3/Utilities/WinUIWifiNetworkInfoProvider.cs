using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.Network;
using Windows.Devices.Geolocation;

namespace SyncClipboard.WinUI3.Utilities;

internal sealed class WinUIWifiNetworkInfoProvider(IThreadDispatcher dispatcher) : IWifiNetworkInfoProvider
{
    private readonly WindowsWifiNetworkInfoProvider _nativeProvider = new();
    private readonly IThreadDispatcher _dispatcher = dispatcher;

    public event EventHandler? NetworkChanged
    {
        add => _nativeProvider.NetworkChanged += value;
        remove => _nativeProvider.NetworkChanged -= value;
    }

    public bool CanOpenWifiSettings => _nativeProvider.CanOpenWifiSettings;

    public async Task<(WifiAccessStatus Status, IReadOnlyList<WifiNetworkInfo> Networks, string? Error)> GetConnectedNetworksAsync(
        bool requestAccess,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requestAccess)
        {
            try
            {
                var access = await _dispatcher.RunOnMainThreadAsync(async () =>
                    await Geolocator.RequestAccessAsync());
                cancellationToken.ThrowIfCancellationRequested();
                if (access == GeolocationAccessStatus.Denied)
                {
                    return (WifiAccessStatus.Denied, [], "Windows location access was denied.");
                }
                if (access != GeolocationAccessStatus.Allowed)
                {
                    return (WifiAccessStatus.Error, [], $"Windows returned location access status: {access}.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (WifiAccessStatus.Error, [], ex.Message);
            }
        }

        return await _nativeProvider.GetConnectedNetworksAsync(requestAccess, cancellationToken).ConfigureAwait(false);
    }

    public void OpenWifiSettings() => _nativeProvider.OpenWifiSettings();
}
