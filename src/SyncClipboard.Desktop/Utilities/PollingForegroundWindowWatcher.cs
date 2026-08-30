using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Threading;

namespace SyncClipboard.Desktop.Utilities;

internal sealed class PollingForegroundWindowWatcher(INativeWindowController foregroundWindowInfoProvider) : INativeForegroundWindowWatcher
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);

    private readonly object _syncRoot = new();
    private Timer? _timer;
    private NativeWindowInfo? _lastWindow;
    private bool _hasLastWindow;

    public event Action<NativeWindowInfo?>? ForegroundWindowChanged;

    public void Start()
    {
        lock (_syncRoot)
        {
            _timer ??= new Timer(OnTimer, null, TimeSpan.Zero, PollingInterval);
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            _timer?.Dispose();
            _timer = null;
            _hasLastWindow = false;
            _lastWindow = null;
        }
    }

    private void OnTimer(object? _)
    {
        var currentWindow = foregroundWindowInfoProvider.GetForegroundWindowDetail()?.NativeWindowInfo;
        lock (_syncRoot)
        {
            if (_hasLastWindow && AreSameWindow(_lastWindow, currentWindow))
            {
                return;
            }

            _hasLastWindow = true;
            _lastWindow = currentWindow;
        }
        ForegroundWindowChanged?.Invoke(currentWindow);
    }

    private static bool AreSameWindow(NativeWindowInfo? left, NativeWindowInfo? right) =>
        left is null ? right is null : right is not null && left.IsSameWindow(right);

    public void Dispose() => Stop();
}
