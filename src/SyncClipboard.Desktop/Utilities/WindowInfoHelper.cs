using SyncClipboard.Core.Models;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal static class WindowInfoHelper
{
    public static ForegroundWindowInfo? GetWindowInfo(IntPtr display, IntPtr window)
    {
        try
        {
            var pid = GetWindowPid(display, window);
            var processName = pid.HasValue ? GetProcessNameFromPid(pid.Value) : null;
            var commandLine = pid.HasValue ? GetCommandLineFromPid(pid.Value) : null;
            var windowTitle = GetWindowTitle(display, window);

            if (string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(windowTitle) && string.IsNullOrEmpty(commandLine))
            {
                return null;
            }

            return new ForegroundWindowInfo
            {
                ProcessName = processName ?? string.Empty,
                WindowTitle = windowTitle ?? string.Empty,
                ExecutableName = commandLine ?? string.Empty
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? GetWindowTitle(IntPtr display, IntPtr window)
    {
        try
        {
            var result = X11Interop.XFetchName(display, window, out var windowNamePtr);
            if (result != 0 && windowNamePtr != IntPtr.Zero)
            {
                var windowName = Marshal.PtrToStringAnsi(windowNamePtr);
                _ = X11Interop.XFree(windowNamePtr);
                return windowName;
            }

            var wmNameAtom = X11Interop.XInternAtom(display, "WM_NAME", false);
            if (wmNameAtom != IntPtr.Zero)
            {
                result = X11Interop.XGetWindowProperty(
                    display, window, wmNameAtom,
                    0, 1024, false, IntPtr.Zero,
                    out var actualType, out var actualFormat,
                    out var nItems, out var bytesAfter,
                    out var propPtr);

                if (result == 0 && propPtr != IntPtr.Zero && nItems > 0)
                {
                    var windowName = Marshal.PtrToStringAnsi(propPtr);
                    _ = X11Interop.XFree(propPtr);
                    return windowName;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static int? GetWindowPid(IntPtr display, IntPtr window)
    {
        try
        {
            var pidAtom = X11Interop.XInternAtom(display, "_NET_WM_PID", false);
            if (pidAtom == IntPtr.Zero)
            {
                return null;
            }

            var result = X11Interop.XGetWindowProperty(
                display, window, pidAtom,
                0, 1, false, IntPtr.Zero,
                out var actualType, out var actualFormat,
                out var nItems, out var bytesAfter,
                out var propPtr);

            if (result == 0 && propPtr != IntPtr.Zero && nItems > 0)
            {
                var pid = Marshal.ReadInt32(propPtr);
                _ = X11Interop.XFree(propPtr);
                return pid;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetProcessNameFromPid(int pid)
    {
        try
        {
            var procPath = $"/proc/{pid}/comm";
            if (File.Exists(procPath))
            {
                return File.ReadAllText(procPath).Trim();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetCommandLineFromPid(int pid)
    {
        try
        {
            var cmdlinePath = $"/proc/{pid}/cmdline";
            if (File.Exists(cmdlinePath))
            {
                var cmdline = File.ReadAllText(cmdlinePath);
                // cmdline is null-separated, get only the first part (executable path)
                var parts = cmdline.Split('\0');
                if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                {
                    return parts[0];
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
