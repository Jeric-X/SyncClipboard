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

internal sealed class MacWifiNetworkInfoProvider : IWifiNetworkInfoProvider, IDisposable
{
    private static readonly CWEventType[] MonitoredEventTypes =
    [
        CWEventType.SsidDidChange,
        CWEventType.LinkDidChange,
        CWEventType.PowerDidChange,
    ];

    private readonly Lock _listenerLock = new();
    private readonly CLLocationManager _locationManager = new();
    private readonly CWWiFiClient _wifiClient = CWWiFiClient.SharedWiFiClient;
    private readonly WifiEventDelegate _wifiEventDelegate;
    private EventHandler? _networkChanged;
    private bool _isMonitoring;
    private bool _isDisposed;

    public MacWifiNetworkInfoProvider()
    {
        _wifiEventDelegate = new WifiEventDelegate(RaiseNetworkChanged);
    }

    public event EventHandler? NetworkChanged
    {
        add
        {
            if (value is null)
            {
                return;
            }

            lock (_listenerLock)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                var startMonitoring = _networkChanged is null;
                _networkChanged += value;
                if (startMonitoring)
                {
                    StartMonitoring();
                }
            }
        }
        remove
        {
            if (value is null)
            {
                return;
            }

            lock (_listenerLock)
            {
                _networkChanged -= value;
                if (_networkChanged is null)
                {
                    StopMonitoring();
                }
            }
        }
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
                .Where(item => !string.IsNullOrEmpty(item.Ssid))
                .Select(item => new WifiNetworkInfo(item.InterfaceName, item.InterfaceName, item.Ssid!))
                .ToList();

            var status = networks.Count > 0
                ? WifiAccessStatus.Available
                : requestAccess ? WifiAccessStatus.Denied : WifiAccessStatus.NotRequested;
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

    public void Dispose()
    {
        lock (_listenerLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _networkChanged = null;
            StopMonitoring();
        }

        _wifiEventDelegate.Dispose();
        _locationManager.Dispose();
    }

    private void StartMonitoring()
    {
        if (_isMonitoring || _isDisposed)
        {
            return;
        }

        try
        {
            _wifiClient.Delegate = _wifiEventDelegate;
            foreach (var eventType in MonitoredEventTypes)
            {
                _isMonitoring |= _wifiClient.StartMonitoringEvent(eventType, out _);
            }

            if (!_isMonitoring)
            {
                _wifiClient.Delegate = null;
            }
        }
        catch
        {
            StopMonitoring();
        }
    }

    private void StopMonitoring()
    {
        try
        {
            if (_isMonitoring)
            {
                _wifiClient.StopMonitoringAllEvents(out _);
            }

            _wifiClient.Delegate = null;
        }
        catch
        {
            // The periodic SSID check remains available when native monitoring cannot be stopped cleanly.
        }
        finally
        {
            _isMonitoring = false;
        }
    }

    private void RaiseNetworkChanged()
    {
        EventHandler? handler;
        lock (_listenerLock)
        {
            handler = _networkChanged;
        }

        handler?.Invoke(this, EventArgs.Empty);
    }

    private sealed class WifiEventDelegate(Action networkChanged) : CWEventDelegate
    {
        public override void SsidDidChangeForWiFi(string interfaceName) => networkChanged();

        public override void LinkDidChangeForWiFi(string interfaceName) => networkChanged();

        public override void PowerStateDidChangeForWiFi(string interfaceName) => networkChanged();

        public override void ClientConnectionInterrupted() => networkChanged();

        public override void ClientConnectionInvalidated() => networkChanged();
    }
}
