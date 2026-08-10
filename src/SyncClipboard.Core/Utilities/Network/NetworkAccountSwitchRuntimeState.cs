namespace SyncClipboard.Core.Utilities.Network;

public sealed class NetworkAccountSwitchRuntimeState
{
    private readonly object _lock = new();
    private bool _manualOverride;
    private string? _lastNetworkFingerprint;

    public bool ManualOverride
    {
        get
        {
            lock (_lock) return _manualOverride;
        }
    }

    public void OnManualSelection()
    {
        lock (_lock) _manualOverride = true;
    }

    public bool OnNetworkChanged()
    {
        lock (_lock)
        {
            var wasOverridden = _manualOverride;
            _manualOverride = false;
            return wasOverridden;
        }
    }

    public bool ShouldClearManualOverride(string fingerprint)
    {
        lock (_lock)
        {
            if (_manualOverride && !string.Equals(fingerprint, _lastNetworkFingerprint, StringComparison.Ordinal))
            {
                _manualOverride = false;
                return true;
            }
            return false;
        }
    }

    public void OnConfigurationChanged()
    {
        lock (_lock)
        {
            _manualOverride = false;
            _lastNetworkFingerprint = null;
        }
    }

    public bool ShouldEvaluate(string fingerprint, bool force)
    {
        lock (_lock)
        {
            if (!force && string.Equals(fingerprint, _lastNetworkFingerprint, StringComparison.Ordinal))
            {
                return false;
            }

            _lastNetworkFingerprint = fingerprint;
            return true;
        }
    }
}
