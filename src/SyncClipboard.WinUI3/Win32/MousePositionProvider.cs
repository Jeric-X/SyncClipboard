using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;

namespace SyncClipboard.WinUI3.Win32;

internal sealed class MousePositionProvider : IMousePositionProvider
{
    public ScreenPosition GetMousePosition()
    {
        try
        {
            if (!User32Interop.GetCursorPos(out var point))
            {
                return ScreenPosition.Invalid;
            }

            return new ScreenPosition
            {
                X = point.X,
                Y = point.Y,
                IsValid = true
            };
        }
        catch (Exception)
        {
            return ScreenPosition.Invalid;
        }
    }
}
