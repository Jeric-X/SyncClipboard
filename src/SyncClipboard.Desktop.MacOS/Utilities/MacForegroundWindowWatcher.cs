using AppKit;
using Foundation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Threading;

namespace SyncClipboard.Desktop.MacOS.Utilities;

internal sealed class MacForegroundWindowWatcher(
    IThreadDispatcher threadDispatcher,
    INativeWindowController foregroundWindowInfoProvider) : INativeForegroundWindowWatcher
{
    private readonly IThreadDispatcher _threadDispatcher = threadDispatcher;
    private NSObject? _observer;
    private Timer? _pollingTimer;
    private NativeWindowInfo? _lastPolledWindow;

    public event Action<NativeWindowInfo?>? ForegroundWindowChanged;

    public void Start()
    {
        if (_observer != null)
        {
            return;
        }

        _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            _observer = NSWorkspace.Notifications.ObserveDidActivateApplication((_, __) =>
            {
                var current = ReadCurrentWindow();
                _lastPolledWindow = current;
                ForegroundWindowChanged?.Invoke(current);
            });
        }).GetAwaiter().GetResult();

        if (MacInterop.AXIsProcessTrusted())
        {
            _lastPolledWindow = ReadCurrentWindow();
            _pollingTimer = new Timer(PollForegroundWindow, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }
    }

    public void Stop()
    {
        if (_observer is null && _pollingTimer is null)
        {
            return;
        }

        _pollingTimer?.Dispose();
        _pollingTimer = null;
        _lastPolledWindow = null;

        _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            _observer?.Dispose();
            _observer = null;
        }).GetAwaiter().GetResult();
    }

    private void PollForegroundWindow(object? state)
    {
        var current = ReadCurrentWindow();
        var previous = _lastPolledWindow;
        _lastPolledWindow = current;

        var changed = current is null
            ? previous is not null
            : previous is null || !current.IsSameWindow(previous);
        if (changed)
        {
            ForegroundWindowChanged?.Invoke(current);
        }
    }

    private NativeWindowInfo? ReadCurrentWindow() =>
        foregroundWindowInfoProvider.GetForegroundWindowDetail()?.NativeWindowInfo;

    public void Dispose() => Stop();
}
