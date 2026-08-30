using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("windows")]
internal sealed class WindowsForegroundWindowWatcher : INativeForegroundWindowWatcher
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0;

    private readonly WinEventDelegate _callback;
    private nint _hook;

    public WindowsForegroundWindowWatcher()
    {
        _callback = OnWinEvent;
    }

    public event Action<NativeWindowInfo?>? ForegroundWindowChanged;

    public void Start()
    {
        if (_hook != nint.Zero)
        {
            return;
        }

        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            nint.Zero,
            _callback,
            0,
            0,
            WinEventOutOfContext);
    }

    public void Stop()
    {
        if (_hook == nint.Zero)
        {
            return;
        }

        _ = UnhookWinEvent(_hook);
        _hook = nint.Zero;
    }

    public void Dispose() => Stop();

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        _ = GetWindowThreadProcessId(window, out var processId);
        ForegroundWindowChanged?.Invoke(processId == 0
            ? null
            : new WindowsNativeWindowInfo
            {
                ProcessId = (int)processId,
                WindowHandle = window
            });
    }

    private delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
