using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace SyncClipboard.Desktop.Utilities.NativeWindowController;

[SupportedOSPlatform("windows")]
internal sealed class WindowsNativeWindowController(ILogger logger) : INativeWindowController
{
    private const string Tag = "ForegroundWindow";
    private const int SwRestore = 9;

    public WindowDetail? GetForegroundWindowDetail()
    {
        try
        {
            var window = GetForegroundWindow();
            var nativeWindow = CreateNativeWindowInfo(window);
            return nativeWindow is null ? null : GetWindowDetail(nativeWindow);
        }
        catch (Exception ex)
        {
            logger.Write(Tag, ex.Message);
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
            if (!IsWindow(hWnd))
            {
                return null;
            }

            _ = GetWindowThreadProcessId(hWnd, out var processId);
            if (processId == 0 || processId != windowsWindow.ProcessId)
            {
                return null;
            }

            string processName = string.Empty;
            string executableName = string.Empty;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
                try
                {
                    executableName = process.MainModule?.ModuleName ?? string.Empty;
                }
                catch (Exception ex)
                {
                    logger.Write(Tag, $"Failed to get MainModule for process {processId}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                logger.Write(Tag, $"Failed to get metadata for process {processId}: {ex.Message}");
            }

            var title = new StringBuilder(256);
            _ = GetWindowText(hWnd, title, title.Capacity);
            ScreenPosition? bounds = null;
            if (GetWindowRect(hWnd, out var rect))
            {
                bounds = new ScreenPosition
                {
                    X = rect.Left,
                    Y = rect.Top,
                    Width = rect.Right - rect.Left,
                    Height = rect.Bottom - rect.Top
                };
            }

            return new WindowDetail
            {
                WindowInfo = new WindowInfo
                {
                    ProcessName = processName,
                    WindowTitle = title.ToString(),
                    ExecutableName = executableName
                },
                Bounds = bounds,
                NativeWindowInfo = windowsWindow
            };
        }
        catch (Exception ex)
        {
            logger.Write(Tag, ex.Message);
            return null;
        }
    }

    public bool TryActivateWindow(NativeWindowInfo window)
    {
        if (window is not WindowsNativeWindowInfo windowsWindow)
        {
            logger.Write(Tag, $"Unsupported native window type: {window.GetType().Name}");
            return false;
        }

        var hWnd = windowsWindow.WindowHandle;
        if (!IsWindow(hWnd))
        {
            logger.Write(Tag, $"Target hwnd={hWnd.ToInt64():X} is no longer valid.");
            return false;
        }

        _ = GetWindowThreadProcessId(hWnd, out var processId);
        if (processId != windowsWindow.ProcessId)
        {
            logger.Write(Tag, $"Target hwnd={hWnd.ToInt64():X} was reused by process {processId}.");
            return false;
        }

        if (IsIconic(hWnd))
        {
            _ = ShowWindowAsync(hWnd, SwRestore);
        }

        if (!SetForegroundWindow(hWnd))
        {
            logger.Write(Tag, $"SetForegroundWindow failed for hwnd={hWnd.ToInt64():X}, error={Marshal.GetLastWin32Error()}.");
            return false;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (GetForegroundWindow() == hWnd)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        logger.Write(Tag, $"Target hwnd={hWnd.ToInt64():X} did not become the foreground window.");
        return false;
    }

    private static WindowsNativeWindowInfo? CreateNativeWindowInfo(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        return processId == 0
            ? null
            : new WindowsNativeWindowInfo
            {
                ProcessId = (int)processId,
                WindowHandle = window
            };
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
