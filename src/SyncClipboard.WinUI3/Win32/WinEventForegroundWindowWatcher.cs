using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.InteropServices;

namespace SyncClipboard.WinUI3.Win32;

internal sealed class WinEventForegroundWindowWatcher : INativeForegroundWindowWatcher
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0;

    private IntPtr _hook;
    private readonly WinEventDelegate _callback;

    public event Action<NativeWindowInfo?>? ForegroundWindowChanged;

    public WinEventForegroundWindowWatcher()
    {
        _callback = OnWinEvent;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WineventOutOfContext);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        _ = UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hWnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        _ = User32Interop.GetWindowThreadProcessId(hWnd, out var processId);
        ForegroundWindowChanged?.Invoke(processId == 0
            ? null
            : new WindowsNativeWindowInfo
            {
                ProcessId = (int)processId,
                WindowHandle = hWnd
            });
    }

    public void Dispose() => Stop();

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
