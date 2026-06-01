using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;

namespace SyncClipboard.WinUI3.Win32;

internal sealed class ForegroundWindowInfoProvider(ILogger logger) : IForegroundWindowInfoProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "ForegroundWindow";

    public ForegroundWindowDetail GetForegroundWindowDetail()
    {
        try
        {
            var hWnd = User32Interop.GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
            {
                _logger.Write(Tag, "GetForegroundWindow returned null");
                return ForegroundWindowDetail.Invalid;
            }

            var windowInfo = WindowInfoHelper.GetWindowInfoFromHwnd(hWnd, _logger, Tag);
            if (windowInfo == null)
            {
                return ForegroundWindowDetail.Invalid;
            }

            if (!User32Interop.GetWindowRect(hWnd, out var rect))
            {
                _logger.Write(Tag, $"GetWindowRect failed for hwnd={hWnd.ToInt64():X}");
                return ForegroundWindowDetail.Invalid;
            }

            return new ForegroundWindowDetail
            {
                WindowInfo = windowInfo,
                X = rect.Left,
                Y = rect.Top,
                Width = rect.Right - rect.Left,
                Height = rect.Bottom - rect.Top,
                IsValid = true
            };
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return ForegroundWindowDetail.Invalid;
        }
    }

    public ForegroundWindowInfo? GetForegroundWindowInfo()
    {
        try
        {
            var hWnd = User32Interop.GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
            {
                _logger.Write(Tag, "GetForegroundWindow returned null");
                return null;
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
