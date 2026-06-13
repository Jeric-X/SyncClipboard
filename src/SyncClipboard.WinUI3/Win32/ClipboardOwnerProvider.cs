using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.Versioning;

namespace SyncClipboard.WinUI3.Win32;

[SupportedOSPlatform("windows")]
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
            var hWnd = User32Interop.GetClipboardOwner();
            if (hWnd == IntPtr.Zero)
            {
                _logger.Write(Tag, "GetClipboardOwner returned null, falling back to foreground window");
                return _foregroundWindowInfoProvider.GetForegroundWindowInfo();
            }

            return WindowInfoHelper.GetWindowInfoFromHwnd(hWnd, _logger, Tag);
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return null;
        }
    }
}
