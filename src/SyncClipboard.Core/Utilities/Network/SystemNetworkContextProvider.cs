using System.Net.NetworkInformation;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Network;

public sealed class SystemNetworkContextProvider(IWifiNetworkInfoProvider wifiProvider, ConfigManager configManager) : INetworkContextProvider, IDisposable
{
    private readonly IWifiNetworkInfoProvider _wifiProvider = wifiProvider;
    private readonly ConfigManager _configManager = configManager;
    private readonly object _listenerLock = new();
    private EventHandler? _networkChanged;
    private bool _isListening;

    public event EventHandler? NetworkChanged
    {
        add
        {
            lock (_listenerLock)
            {
                _networkChanged += value;
                if (_isListening || _networkChanged is null) return;
                StartListening();
            }
        }
        remove
        {
            lock (_listenerLock)
            {
                _networkChanged -= value;
                if (!_isListening || _networkChanged is not null) return;
                StopListening();
            }
        }
    }

    public bool CanOpenWifiSettings => _wifiProvider.CanOpenWifiSettings;

    public async Task<NetworkContextSnapshot> GetCurrentAsync(bool requestWifiAccess = false, CancellationToken cancellationToken = default)
    {
        var wifiAccessRequested = _configManager.GetConfig<NetworkAccountSwitchConfig>().WifiAccessRequested;
        (WifiAccessStatus Status, IReadOnlyList<WifiNetworkInfo> Networks, string? Error) wifiResult;
        if (!requestWifiAccess && !wifiAccessRequested)
        {
            wifiResult = (WifiAccessStatus.NotRequested, [], null);
        }
        else
        {
            wifiResult = await _wifiProvider.GetConnectedNetworksAsync(requestWifiAccess, cancellationToken).ConfigureAwait(false);
        }

        var interfaces = await Task.Run(
            () => GetNetworkInterfaces(wifiResult.Networks, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return new NetworkContextSnapshot
        {
            Interfaces = interfaces,
            WifiStatus = wifiResult.Status,
            WifiError = wifiResult.Error,
        };
    }

    private static List<NetworkInterfaceSnapshot> GetNetworkInterfaces(
        IReadOnlyList<WifiNetworkInfo> wifiNetworks,
        CancellationToken cancellationToken)
    {
        var interfaces = new List<NetworkInterfaceSnapshot>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch
            {
                continue;
            }

            var addresses = properties.UnicastAddresses
                .Select(item => item.Address)
                .Where(NetworkRuleMatcher.IsAddressAllowed)
                .Distinct()
                .ToList();

            var wifi = wifiNetworks.FirstOrDefault(item =>
                InterfaceIdEquals(item.InterfaceId, networkInterface.Id)
                || string.Equals(item.InterfaceName, networkInterface.Name, StringComparison.Ordinal));

            interfaces.Add(new NetworkInterfaceSnapshot
            {
                Id = networkInterface.Id,
                Name = networkInterface.Name,
                Description = networkInterface.Description,
                HasDefaultGateway = properties.GatewayAddresses.Any(item => NetworkRuleMatcher.IsValidGateway(item.Address)),
                Addresses = addresses,
                WifiSsid = wifi?.Ssid,
            });
        }

        return interfaces;
    }

    public void OpenWifiSettings() => _wifiProvider.OpenWifiSettings();

    private void StartListening()
    {
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _wifiProvider.NetworkChanged += OnWifiNetworkChanged;
        _isListening = true;
    }

    private void StopListening()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _wifiProvider.NetworkChanged -= OnWifiNetworkChanged;
        _isListening = false;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => RaiseNetworkChanged();
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => RaiseNetworkChanged();
    private void OnWifiNetworkChanged(object? sender, EventArgs e) => RaiseNetworkChanged();

    private void RaiseNetworkChanged()
    {
        EventHandler? handler;
        lock (_listenerLock) handler = _networkChanged;
        handler?.Invoke(this, EventArgs.Empty);
    }

    private static bool InterfaceIdEquals(string left, string right) =>
        string.Equals(left.Trim('{', '}'), right.Trim('{', '}'), StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        lock (_listenerLock)
        {
            if (_isListening) StopListening();
            _networkChanged = null;
        }
        if (_wifiProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
