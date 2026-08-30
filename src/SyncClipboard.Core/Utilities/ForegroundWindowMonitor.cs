using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities;

public sealed class ForegroundWindowMonitor(
    INativeForegroundWindowWatcher watcher,
    INativeWindowController provider,
    ILogger logger) : IForegroundWindowMonitor, IDisposable
{
    private const string Tag = "ForegroundWindowMonitor";
    private readonly object _syncRoot = new();
    private Action<WindowDetail?>? _foregroundWindowChanged;
    private bool _isWatching;
    private bool _disposed;

    public event Action<WindowDetail?>? ForegroundWindowChanged
    {
        add
        {
            if (value is null)
            {
                return;
            }

            lock (_syncRoot)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var shouldStart = _foregroundWindowChanged is null;
                _foregroundWindowChanged += value;
                if (shouldStart)
                {
                    StartWatching();
                }
            }
        }
        remove
        {
            if (value is null)
            {
                return;
            }

            lock (_syncRoot)
            {
                _foregroundWindowChanged -= value;
                if (_foregroundWindowChanged is null)
                {
                    StopWatching();
                }
            }
        }
    }

    public WindowDetail? GetCurrentForegroundWindow()
    {
        try
        {
            return provider.GetForegroundWindowDetail();
        }
        catch (Exception ex)
        {
            logger.Write(Tag, $"Failed to read the current foreground window: {ex.Message}");
            return null;
        }
    }

    private void StartWatching()
    {
        if (_isWatching)
        {
            return;
        }

        watcher.ForegroundWindowChanged += OnNativeForegroundWindowChanged;
        watcher.Start();
        _isWatching = true;
    }

    private void StopWatching()
    {
        if (!_isWatching)
        {
            return;
        }

        watcher.Stop();
        watcher.ForegroundWindowChanged -= OnNativeForegroundWindowChanged;
        _isWatching = false;
    }

    private void OnNativeForegroundWindowChanged(NativeWindowInfo? nativeWindow)
    {
        WindowDetail? snapshot;
        try
        {
            snapshot = nativeWindow is null
                ? provider.GetForegroundWindowDetail()
                : provider.GetWindowDetail(nativeWindow);
        }
        catch (Exception ex)
        {
            logger.Write(Tag, $"Failed to read the changed foreground window: {ex.Message}");
            snapshot = null;
        }

        Delegate[] callbacks;
        lock (_syncRoot)
        {
            callbacks = _foregroundWindowChanged?.GetInvocationList() ?? [];
        }

        foreach (var callback in callbacks)
        {
            try
            {
                ((Action<WindowDetail?>)callback)(snapshot);
            }
            catch (Exception ex)
            {
                logger.Write(Tag, $"Foreground window subscriber failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopWatching();
            _foregroundWindowChanged = null;
        }
    }
}
