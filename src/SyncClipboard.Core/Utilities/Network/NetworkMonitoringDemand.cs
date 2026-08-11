namespace SyncClipboard.Core.Utilities.Network;

public readonly record struct NetworkMonitoringDemand(bool ListenForNetworkChanges, bool PollWifi)
{
    public static NetworkMonitoringDemand Calculate(
        bool autoSwitchEnabled,
        bool hasEnabledWifiRules,
        bool hasStatusListeners)
    {
        var listen = autoSwitchEnabled || hasStatusListeners;
        var pollWifi = listen && (hasStatusListeners || hasEnabledWifiRules);
        return new(listen, pollWifi);
    }
}
