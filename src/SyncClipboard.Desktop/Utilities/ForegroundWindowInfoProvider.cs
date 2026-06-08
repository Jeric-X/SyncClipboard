using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class ForegroundWindowInfoProvider(ILogger logger) : IForegroundWindowInfoProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "ForegroundWindowInfo";

    public ForegroundWindowDetail? GetForegroundWindowDetail()
    {
        if (!X11Interop.IsAvailable)
        {
            _logger.Write(Tag, "X11 library not available");
            return null;
        }

        nint display = nint.Zero;
        try
        {
            display = X11Interop.XOpenDisplay(nint.Zero);
            if (display == nint.Zero)
            {
                return null;
            }

            // 获取焦点窗口
            _ = X11Interop.XGetInputFocus(display, out var window, out var revertTo);

            if (window == nint.Zero)
            {
                return null;
            }

            // 找到顶层窗口
            var topLevelWindow = FindTopLevelWindow(display, window);
            if (topLevelWindow == nint.Zero)
            {
                topLevelWindow = window;
            }

            // 获取窗口属性
            if (X11Interop.XGetWindowAttributes(display, topLevelWindow, out var attributes) == 0)
            {
                return null;
            }

            var windowInfo = WindowInfoHelper.GetWindowInfo(display, topLevelWindow);

            return new ForegroundWindowDetail
            {
                WindowInfo = windowInfo,
                Bounds = new ScreenPosition
                {
                    X = attributes.x,
                    Y = attributes.y,
                    Width = attributes.width,
                    Height = attributes.height
                }
            };
        }
        catch (DllNotFoundException ex)
        {
            _logger.Write(Tag, $"DllNotFoundException: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return null;
        }
        finally
        {
            if (display != nint.Zero)
            {
                _ = X11Interop.XCloseDisplay(display);
            }
        }
    }

    public ForegroundWindowInfo? GetForegroundWindowInfo()
    {
        if (!X11Interop.IsAvailable)
        {
            _logger.Write(Tag, "X11 library not available");
            return null;
        }

        nint display = nint.Zero;
        try
        {
            display = X11Interop.XOpenDisplay(nint.Zero);
            if (display == nint.Zero)
            {
                return null;
            }

            _ = X11Interop.XGetInputFocus(display, out var window, out _);
            if (window == nint.Zero)
            {
                return null;
            }

            // 找到顶层窗口
            var topLevelWindow = FindTopLevelWindow(display, window);
            if (topLevelWindow == nint.Zero)
            {
                topLevelWindow = window;
            }

            return WindowInfoHelper.GetWindowInfo(display, topLevelWindow);
        }
        catch (DllNotFoundException ex)
        {
            _logger.Write(Tag, $"DllNotFoundException: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return null;
        }
        finally
        {
            if (display != nint.Zero)
            {
                _ = X11Interop.XCloseDisplay(display);
            }
        }
    }

    private static IntPtr FindTopLevelWindow(IntPtr display, IntPtr window)
    {
        var rootWindow = X11Interop.XDefaultRootWindow(display);
        if (rootWindow == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var currentWindow = window;
        var maxIterations = 100; // 防止无限循环

        for (int i = 0; i < maxIterations; i++)
        {
            var result = X11Interop.XQueryTree(
                display,
                currentWindow,
                out _,
                out var parent,
                out _,
                out _);

            if (result == 0)
            {
                break;
            }

            // 如果父窗口是根窗口，说明当前窗口就是顶层窗口
            if (parent == rootWindow || parent == IntPtr.Zero)
            {
                return currentWindow;
            }

            currentWindow = parent;
        }

        return currentWindow;
    }
}
