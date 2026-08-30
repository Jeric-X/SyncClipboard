using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Desktop.Utilities;
using System;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities.ForegroundWindowInfoProvider;

[SupportedOSPlatform("linux")]
internal sealed class LinuxForegroundWindowInfoProvider(ILogger logger) : IForegroundWindowInfoProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "ForegroundWindowInfo";
    private const int ClientMessage = 33;
    private const long SubstructureNotifyMask = 1L << 19;
    private const long SubstructureRedirectMask = 1L << 20;

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

            var nativeWindow = new X11NativeWindowInfo
            {
                ProcessId = WindowInfoHelper.GetWindowPid(display, topLevelWindow) ?? 0,
                DisplayName = Environment.GetEnvironmentVariable("DISPLAY") ?? string.Empty,
                WindowId = (nuint)topLevelWindow
            };
            return BuildWindowDetail(display, nativeWindow);
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
        return GetForegroundWindowDetail()?.WindowInfo;
    }

    public ForegroundWindowDetail? GetWindowDetail(NativeWindowInfo window)
    {
        if (window is not X11NativeWindowInfo x11Window || !X11Interop.IsAvailable)
        {
            return null;
        }

        nint display = nint.Zero;
        try
        {
            display = X11Interop.XOpenDisplay(string.IsNullOrEmpty(x11Window.DisplayName) ? null : x11Window.DisplayName);
            return display == nint.Zero ? null : BuildWindowDetail(display, x11Window);
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Failed to read X11 window: {ex.Message}");
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

    public bool TryActivateWindow(NativeWindowInfo window)
    {
        if (window is not X11NativeWindowInfo x11Window || !X11Interop.IsAvailable)
        {
            _logger.Write(Tag, $"Unsupported native window type: {window.GetType().Name}");
            return false;
        }

        nint display = nint.Zero;
        try
        {
            display = X11Interop.XOpenDisplay(string.IsNullOrEmpty(x11Window.DisplayName) ? null : x11Window.DisplayName);
            var target = (nint)x11Window.WindowId;
            if (display == nint.Zero || X11Interop.XGetWindowAttributes(display, target, out _) == 0)
            {
                _logger.Write(Tag, $"X11 target window {x11Window.WindowId} is no longer valid.");
                return false;
            }

            var root = X11Interop.XDefaultRootWindow(display);
            var activeWindowAtom = X11Interop.XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
            if (root == nint.Zero || activeWindowAtom == nint.Zero)
            {
                _logger.Write(Tag, "Failed to resolve X11 root window or _NET_ACTIVE_WINDOW atom.");
                return false;
            }

            var message = new XEvent
            {
                ClientMessage = new XClientMessageEvent
                {
                    type = ClientMessage,
                    send_event = 1,
                    display = display,
                    window = target,
                    message_type = activeWindowAtom,
                    format = 32,
                    data = new XClientMessageData { l0 = 2 }
                }
            };

            var eventMask = (nint)(SubstructureNotifyMask | SubstructureRedirectMask);
            var sent = X11Interop.XSendEvent(display, root, false, eventMask, ref message) != 0;
            _ = X11Interop.XFlush(display);
            if (!sent)
            {
                _logger.Write(Tag, $"Failed to send _NET_ACTIVE_WINDOW for X11 window {x11Window.WindowId}.");
            }
            return sent;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Failed to activate X11 window: {ex.Message}");
            return false;
        }
        finally
        {
            if (display != nint.Zero)
            {
                _ = X11Interop.XCloseDisplay(display);
            }
        }
    }

    private static ForegroundWindowDetail? BuildWindowDetail(nint display, X11NativeWindowInfo nativeWindow)
    {
        var window = (nint)nativeWindow.WindowId;
        if (X11Interop.XGetWindowAttributes(display, window, out var attributes) == 0)
        {
            return null;
        }

        var currentPid = WindowInfoHelper.GetWindowPid(display, window) ?? 0;
        if (nativeWindow.ProcessId != 0 && currentPid != 0 && nativeWindow.ProcessId != currentPid)
        {
            return null;
        }

        var normalizedNativeWindow = nativeWindow with { ProcessId = currentPid };
        return new ForegroundWindowDetail
        {
            WindowInfo = WindowInfoHelper.GetWindowInfo(display, window),
            Bounds = new ScreenPosition
            {
                X = attributes.x,
                Y = attributes.y,
                Width = attributes.width,
                Height = attributes.height
            },
            NativeWindowInfo = normalizedNativeWindow
        };
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
