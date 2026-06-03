using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class ClipboardOwnerProvider(
    ILogger logger,
    IForegroundWindowInfoProvider foregroundWindowInfoProvider) : IClipboardOwnerProvider
{
    private readonly ILogger _logger = logger;
    private readonly IForegroundWindowInfoProvider _foregroundWindowInfoProvider = foregroundWindowInfoProvider;
    private const string Tag = "ClipboardOwner";

    public ForegroundWindowInfo? GetClipboardOwner()
    {
        try
        {
            var owner = GetX11ClipboardOwner();
            if (owner == null)
            {
                _logger.Write(Tag, "Failed to get X11 clipboard owner, falling back to foreground window");
                return _foregroundWindowInfoProvider.GetForegroundWindowInfo();
            }

            return owner;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return _foregroundWindowInfoProvider.GetForegroundWindowInfo();
        }
    }

    private ForegroundWindowInfo? GetX11ClipboardOwner()
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
                _logger.Write(Tag, "Failed to open X11 display");
                return null;
            }

            var clipboardAtom = X11Interop.XInternAtom(display, "CLIPBOARD", false);
            if (clipboardAtom == nint.Zero)
            {
                _logger.Write(Tag, "Failed to get CLIPBOARD atom");
                return null;
            }

            var ownerWindow = X11Interop.XGetSelectionOwner(display, clipboardAtom);
            if (ownerWindow == nint.Zero)
            {
                _logger.Write(Tag, "No clipboard owner found");
                return null;
            }

            return WindowInfoHelper.GetWindowInfo(display, ownerWindow);
        }
        catch (DllNotFoundException ex)
        {
            _logger.Write(Tag, $"DllNotFoundException: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception in GetX11ClipboardOwner: {ex.Message}");
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
