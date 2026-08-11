using System.Collections.ObjectModel;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.UserServices;

namespace SyncClipboard.Core.ViewModels;

public sealed record NetworkAddressStatusItem(string Value);

public sealed record NetworkConnectionStatusItem(
    string DisplayName,
    string GatewayStatusText,
    bool HasDefaultGateway,
    string WifiSsid,
    IReadOnlyList<NetworkAddressStatusItem> Ipv4Addresses,
    IReadOnlyList<NetworkAddressStatusItem> Ipv6Addresses)
{
    public bool HasWifi => !string.IsNullOrWhiteSpace(WifiSsid);
    public bool HasIpv4 => Ipv4Addresses.Count > 0;
    public bool HasIpv6 => Ipv6Addresses.Count > 0;
}

public partial class CurrentNetworkStatusViewModel : ObservableObject
{
    private readonly NetworkAccountSwitchService _service;
    private readonly IThreadDispatcher _dispatcher;
    private bool _active;

    [ObservableProperty] private string summaryText = Strings.NoNetworkConnections;
    [ObservableProperty] private bool showEmptyState = true;

    public ObservableCollection<NetworkConnectionStatusItem> Connections { get; } = [];

    public CurrentNetworkStatusViewModel(NetworkAccountSwitchService service, IThreadDispatcher dispatcher)
    {
        _service = service;
        _dispatcher = dispatcher;
        UpdateSnapshot();
    }

    public void Activate()
    {
        if (_active) return;
        _active = true;
        _service.StatusChanged += OnServiceStatusChanged;
        _service.AddNetworkMonitoringDemand();
        UpdateSnapshot();
        _ = RefreshAsync();
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false;
        _service.RemoveNetworkMonitoringDemand();
        _service.StatusChanged -= OnServiceStatusChanged;
    }

    [RelayCommand]
    private async Task Refresh() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            await _service.RefreshAsync();
            if (_active) await _dispatcher.RunOnMainThreadAsync(UpdateSnapshot);
        }
        catch (OperationCanceledException) when (!_active) { }
    }

    private void OnServiceStatusChanged(object? sender, EventArgs e) =>
        _ = _dispatcher.RunOnMainThreadAsync(() =>
        {
            if (_active) UpdateSnapshot();
        });

    private void UpdateSnapshot()
    {
        Connections.Clear();
        foreach (var networkInterface in _service.Snapshot.Interfaces
            .OrderByDescending(item => item.HasDefaultGateway)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var ipv4 = networkInterface.Addresses
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => new NetworkAddressStatusItem(address.ToString()))
                .ToArray();
            var ipv6 = networkInterface.Addresses
                .Where(address => address.AddressFamily == AddressFamily.InterNetworkV6)
                .Select(address => new NetworkAddressStatusItem(address.ToString()))
                .ToArray();

            Connections.Add(new(
                networkInterface.DisplayName,
                networkInterface.HasDefaultGateway
                    ? Strings.DefaultGatewayConnection
                    : Strings.NoDefaultGateway,
                networkInterface.HasDefaultGateway,
                networkInterface.WifiSsid ?? string.Empty,
                ipv4,
                ipv6));
        }

        ShowEmptyState = Connections.Count == 0;
        SummaryText = ShowEmptyState
            ? Strings.NoNetworkConnections
            : string.Format(Strings.NetworkConnectionCount, Connections.Count);
    }
}
