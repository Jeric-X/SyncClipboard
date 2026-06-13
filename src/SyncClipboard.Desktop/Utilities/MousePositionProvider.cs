using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class MousePositionProvider(ILogger logger) : IMousePositionProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "MousePosition";

    public ScreenPosition? GetMousePosition()
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

            var rootWindow = X11Interop.XDefaultRootWindow(display);
            if (rootWindow == nint.Zero)
            {
                return null;
            }

            int result = X11Interop.XQueryPointer(
                display,
                rootWindow,
                out _,
                out _,
                out int rootX,
                out int rootY,
                out _,
                out _,
                out _);

            if (result == 0)
            {
                return null;
            }

            return new ScreenPosition
            {
                X = rootX,
                Y = rootY
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
}
