using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.UserServices;

public sealed class ForegroundWindowTrackingService : Service
{
    private const string Tag = "ForegroundWindowTracking";
    private readonly IForegroundWindowMonitor _monitor;
    private readonly INativeWindowController _provider;
    private readonly ILogger _logger;
    private readonly bool _trackingEnabled;
    private readonly object _syncRoot = new();
    private WindowDetail? _currentPasteTargetWindow;
    private WindowDetail? _previousPasteTargetWindow;
    private NativeWindowInfo? _historyWindow;
    private NativeWindowInfo? _lastActivationTarget;

    public ForegroundWindowTrackingService(
        IForegroundWindowMonitor monitor,
        INativeWindowController provider,
        ILogger logger)
        : this(monitor, provider, logger, !OperatingSystem.IsLinux())
    {
    }

    internal ForegroundWindowTrackingService(
        IForegroundWindowMonitor monitor,
        INativeWindowController provider,
        ILogger logger,
        bool trackingEnabled)
    {
        _monitor = monitor;
        _provider = provider;
        _logger = logger;
        _trackingEnabled = trackingEnabled;
    }

    public WindowDetail? LastExternalWindow
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentPasteTargetWindow;
            }
        }
    }

    public void CaptureCurrentForegroundWindow()
    {
        if (!_trackingEnabled)
        {
            return;
        }

        var current = _monitor.GetCurrentForegroundWindow();
        _logger.Write(Tag, $"Foreground window captured on demand: {DescribeWindow(current?.NativeWindowInfo)}.");
        OnForegroundWindowChanged(current);
    }

    protected override void StartService()
    {
        if (!_trackingEnabled)
        {
            _logger.Write(Tag, "Foreground window tracking is disabled on this platform.");
            return;
        }

        _monitor.ForegroundWindowChanged += OnForegroundWindowChanged;
        var current = _monitor.GetCurrentForegroundWindow();
        _logger.Write(Tag, $"Tracking started; current={DescribeWindow(current?.NativeWindowInfo)}.");
        OnForegroundWindowChanged(current);
    }

    protected override void StopSerivce()
    {
        if (_trackingEnabled)
        {
            _monitor.ForegroundWindowChanged -= OnForegroundWindowChanged;
        }
    }

    public void SetHistoryWindow(NativeWindowInfo? historyWindow)
    {
        if (historyWindow is null)
        {
            return;
        }

        lock (_syncRoot)
        {
            _historyWindow = historyWindow;
            if (_currentPasteTargetWindow?.NativeWindowInfo is { } last
                && IsHistoryWindow(historyWindow, last))
            {
                _currentPasteTargetWindow = _previousPasteTargetWindow;
                _previousPasteTargetWindow = null;
            }
        }

        _logger.Write(Tag, $"History window registered: {DescribeWindow(historyWindow)}.");
    }

    public bool TryActivateLastExternalWindow()
    {
        NativeWindowInfo? target;
        lock (_syncRoot)
        {
            target = _currentPasteTargetWindow?.NativeWindowInfo;
            _lastActivationTarget = target;
        }

        if (target is null)
        {
            _logger.Write(Tag, "No restorable foreground window has been recorded.");
            return false;
        }

        _logger.Write(Tag, $"Activating recorded target: {DescribeWindow(target)}.");
        var activated = _provider.TryActivateWindow(target);
        _logger.Write(Tag, $"Activation request result={activated}: {DescribeWindow(target)}.");
        return activated;
    }

    public bool IsLastActivationTargetForeground()
    {
        NativeWindowInfo? target;
        lock (_syncRoot)
        {
            target = _lastActivationTarget;
        }

        return target is not null && IsForegroundWindow(target);
    }

    public bool IsHistoryWindowForeground()
    {
        NativeWindowInfo? historyWindow;
        lock (_syncRoot)
        {
            historyWindow = _historyWindow;
        }

        if (historyWindow is null)
        {
            return false;
        }

        var current = GetCurrentForegroundNativeWindow();
        if (current is null)
        {
            return false;
        }

        if (IsHistoryWindow(historyWindow, current))
        {
            return true;
        }

        return false;
    }

    private bool IsForegroundWindow(NativeWindowInfo expected)
    {
        var current = GetCurrentForegroundNativeWindow();
        if (current is null)
        {
            return false;
        }

        if (expected.IsSameWindow(current))
        {
            return true;
        }

        // If the target application exposes no Accessibility window identity,
        // confirming its process as frontmost is the strongest available signal.
        return expected is MacNativeWindowInfo expectedMac
            && current is MacNativeWindowInfo currentMac
            && expectedMac.ProcessId == currentMac.ProcessId
            && !HasMacWindowIdentity(expectedMac);
    }

    private static bool HasMacWindowIdentity(MacNativeWindowInfo window) =>
        window.WindowNumber.HasValue
        || !string.IsNullOrEmpty(window.WindowTitle);

    private NativeWindowInfo? GetCurrentForegroundNativeWindow()
    {
        try
        {
            return _provider.GetForegroundWindowDetail()?.NativeWindowInfo;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Failed to verify the foreground window: {ex.Message}");
            return null;
        }
    }

    private void OnForegroundWindowChanged(WindowDetail? detail)
    {
        if (detail?.NativeWindowInfo is not { } nativeWindow)
        {
            _logger.Write(Tag, "Foreground change could not be identified; keeping the previous target.");
            return;
        }

        lock (_syncRoot)
        {
            if (_historyWindow is not null && IsHistoryWindow(_historyWindow, nativeWindow))
            {
                _logger.Write(Tag, $"Foreground change identified as history window and ignored: {DescribeWindow(nativeWindow)}.");
                return;
            }

            if (_currentPasteTargetWindow?.NativeWindowInfo is { } previous
                && !previous.IsSameWindow(nativeWindow))
            {
                _previousPasteTargetWindow = _currentPasteTargetWindow;
            }
            _currentPasteTargetWindow = detail;
        }

        _logger.Write(Tag, $"Recorded paste target: {DescribeWindow(nativeWindow)}.");
    }

    private static bool IsHistoryWindow(NativeWindowInfo historyWindow, NativeWindowInfo candidate)
    {
        if (historyWindow.IsSameWindow(candidate))
        {
            return true;
        }

        if (historyWindow is MacNativeWindowInfo historyMac
            && candidate is MacNativeWindowInfo candidateMac
            && historyMac.ProcessId == candidateMac.ProcessId)
        {
            if (historyMac.WindowNumber.HasValue && candidateMac.WindowNumber.HasValue)
            {
                return false;
            }

            // AXWindowNumber isn't exposed consistently for Avalonia windows.
            // The history title is stable and unique inside this process, while
            // its bounds can change whenever it is repositioned near the caret.
            if (!string.IsNullOrEmpty(historyMac.WindowTitle)
                && string.Equals(historyMac.WindowTitle, candidateMac.WindowTitle, StringComparison.Ordinal))
            {
                return true;
            }

            return !candidateMac.WindowNumber.HasValue
                && string.IsNullOrEmpty(candidateMac.WindowTitle);
        }

        // When Accessibility permission is unavailable, macOS only exposes the
        // frontmost process. Prefer refusing a paste over recording an unknown
        // SyncClipboard window as the external target.
        return false;
    }

    private static string DescribeWindow(NativeWindowInfo? window) => window switch
    {
        null => "null",
        WindowsNativeWindowInfo windows => $"Windows(pid={windows.ProcessId}, hwnd=0x{windows.WindowHandle.ToInt64():X})",
        X11NativeWindowInfo x11 => $"X11(pid={x11.ProcessId}, display={x11.DisplayName}, id={x11.WindowId})",
        MacNativeWindowInfo mac =>
            $"macOS(pid={mac.ProcessId}, window={mac.WindowNumber?.ToString() ?? "null"}, title='{mac.WindowTitle}', bounds={DescribeBounds(mac.Bounds)})",
        _ => $"{window.GetType().Name}(pid={window.ProcessId})"
    };

    private static string DescribeBounds(ScreenPosition? bounds) => bounds is null
        ? "null"
        : $"{bounds.X},{bounds.Y},{bounds.Width}x{bounds.Height}";
}
