using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.UserServices;

public sealed class HotkeyBlacklistService : Service
{
    private const string Tag = "HotkeyBlacklist";
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _debounceCancellation;
    private HotkeyBlacklistConfig _config = new();
    private bool _isBlacklisted;
    private bool _isMonitoring;

    private readonly IForegroundWindowMonitor _foregroundWindowMonitor;
    private readonly HotkeyManager _hotkeyManager;
    private readonly ILogger _logger;

    public event Action<WindowInfo?>? ForegroundWindowChanged;

    public HotkeyBlacklistService(
        ConfigManager configManager,
        IForegroundWindowMonitor foregroundWindowMonitor,
        HotkeyManager hotkeyManager,
        ILogger logger)
    {
        _foregroundWindowMonitor = foregroundWindowMonitor;
        _hotkeyManager = hotkeyManager;
        _logger = logger;
        configManager.GetAndListenConfig<HotkeyBlacklistConfig>(ConfigChanged);
    }

    protected override void StartService()
    {
        EnsureWatcherState();
    }

    protected override void StopSerivce()
    {
        StopForegroundWindowMonitoring();
    }

    public override void Load()
    {
        EnsureWatcherState();
    }

    private void ConfigChanged(HotkeyBlacklistConfig config)
    {
        _config = config;
        EnsureWatcherState();
    }

    private void OnForegroundWindowChanged(WindowDetail? _) => QueueForegroundWindowCheck();

    private void QueueForegroundWindowCheck()
    {
        if (!ShouldWatchForegroundWindow())
        {
            return;
        }

        CancellationTokenSource cancellation;
        lock (_syncRoot)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            _debounceCancellation = new CancellationTokenSource();
            cancellation = _debounceCancellation;
        }

        _ = CheckAfterDebounceAsync(cancellation.Token);
    }

    private void EnsureWatcherState()
    {
        if (ShouldWatchForegroundWindow())
        {
            StartForegroundWindowMonitoring();
        }
        else
        {
            StopForegroundWindowMonitoring();
        }
    }

    private bool ShouldWatchForegroundWindow()
    {
        return Enabled && _config.Enabled && _config.BlackList.Count > 0;
    }

    private void StartForegroundWindowMonitoring()
    {
        if (!_isMonitoring)
        {
            _foregroundWindowMonitor.ForegroundWindowChanged += OnForegroundWindowChanged;
            _isMonitoring = true;
        }

        QueueForegroundWindowCheck();
    }

    private void StopForegroundWindowMonitoring()
    {
        if (_isMonitoring)
        {
            _foregroundWindowMonitor.ForegroundWindowChanged -= OnForegroundWindowChanged;
            _isMonitoring = false;
        }

        CancelDebounce();
        ResumeSystemHotkeys();
    }

    private async Task CheckAfterDebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            EvaluateForegroundWindow();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, ex.Message);
        }
    }

    private void ResumeSystemHotkeys()
    {
        if (!_isBlacklisted && !_hotkeyManager.IsSystemHotkeysSuspended)
        {
            return;
        }

        _isBlacklisted = false;
        _hotkeyManager.ResumeSystemHotkeys();
    }

    private void EvaluateForegroundWindow()
    {
        if (!ShouldWatchForegroundWindow())
        {
            ResumeSystemHotkeys();
            return;
        }

        var window = _foregroundWindowMonitor.GetCurrentForegroundWindow()?.WindowInfo;
        ForegroundWindowChanged?.Invoke(window);

        var isBlacklisted = _config.Enabled
            && window.HasValue
            && _config.BlackList.Any(item => ForegroundWindowMatcher.Matches(item, window.Value));

        if (isBlacklisted == _isBlacklisted)
        {
            return;
        }

        _isBlacklisted = isBlacklisted;
        if (isBlacklisted)
        {
            _logger.Write(Tag, "Foreground application is blacklisted; suspending system hotkeys.");
            _hotkeyManager.SuspendSystemHotkeys();
        }
        else
        {
            _logger.Write(Tag, "Foreground application left blacklist; resuming system hotkeys.");
            _hotkeyManager.ResumeSystemHotkeys();
        }
    }

    private void CancelDebounce()
    {
        lock (_syncRoot)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            _debounceCancellation = null;
        }
    }
}
