using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Threading;

namespace SyncClipboard.WinUI3.Win32;

internal sealed class Win32NativeWindowController(ILogger logger) : INativeWindowController
{
    private readonly ILogger _logger = logger;
    private const string Tag = "ForegroundWindow";
    private const int SwRestore = 9;

    public WindowDetail? GetForegroundWindowDetail()
    {
        try
        {
            var hWnd = User32Interop.GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
            {
                _logger.Write(Tag, "GetForegroundWindow returned null");
                return null;
            }

            var nativeWindow = CreateNativeWindowInfo(hWnd);
            if (nativeWindow is null)
            {
                return null;
            }
            return GetWindowDetail(nativeWindow);
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return null;
        }
    }

    public WindowInfo? GetForegroundWindowInfo()
    {
        return GetForegroundWindowDetail()?.WindowInfo;
    }

    public WindowDetail? GetWindowDetail(NativeWindowInfo window)
    {
        if (window is not WindowsNativeWindowInfo windowsWindow)
        {
            return null;
        }

        try
        {
            var hWnd = windowsWindow.WindowHandle;
            if (!User32Interop.IsWindow(hWnd))
            {
                return null;
            }

            _ = User32Interop.GetWindowThreadProcessId(hWnd, out var processId);
            if (processId == 0 || processId != windowsWindow.ProcessId)
            {
                return null;
            }

            var result = new WindowDetail
            {
                WindowInfo = WindowInfoHelper.GetWindowInfoFromHwnd(hWnd, _logger, Tag),
                NativeWindowInfo = windowsWindow
            };

            if (User32Interop.GetWindowRect(hWnd, out var rect))
            {
                result = new WindowDetail
                {
                    WindowInfo = result.WindowInfo,
                    NativeWindowInfo = windowsWindow,
                    Bounds = new ScreenPosition
                    {
                        X = rect.Left,
                        Y = rect.Top,
                        Width = rect.Right - rect.Left,
                        Height = rect.Bottom - rect.Top
                    }
                };
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return null;
        }
    }

    public bool TryActivateWindow(NativeWindowInfo window)
    {
        if (window is not WindowsNativeWindowInfo windowsWindow)
        {
            _logger.Write(Tag, $"Unsupported native window type: {window.GetType().Name}");
            return false;
        }

        var hWnd = windowsWindow.WindowHandle;
        if (!User32Interop.IsWindow(hWnd))
        {
            _logger.Write(Tag, $"Target hwnd={hWnd.ToInt64():X} is no longer valid.");
            return false;
        }

        _ = User32Interop.GetWindowThreadProcessId(hWnd, out var processId);
        if (processId != windowsWindow.ProcessId)
        {
            _logger.Write(Tag, $"Target hwnd={hWnd.ToInt64():X} was reused by process {processId}.");
            return false;
        }

        if (User32Interop.IsIconic(hWnd))
        {
            _ = User32Interop.ShowWindowAsync(hWnd, SwRestore);
        }

        if (!User32Interop.SetForegroundWindow(hWnd))
        {
            _logger.Write(Tag, $"SetForegroundWindow failed for hwnd={hWnd.ToInt64():X}, error={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}.");
            return false;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (User32Interop.GetForegroundWindow() == hWnd)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        _logger.Write(Tag, $"Target hwnd={hWnd.ToInt64():X} did not become the foreground window.");
        return false;
    }

    private static WindowsNativeWindowInfo? CreateNativeWindowInfo(IntPtr hWnd)
    {
        _ = User32Interop.GetWindowThreadProcessId(hWnd, out var processId);
        return processId == 0
            ? null
            : new WindowsNativeWindowInfo
            {
                ProcessId = (int)processId,
                WindowHandle = hWnd
            };
    }
}
