using System;
using System.Runtime.Versioning;
using AppKit;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.MacOS.Utilities;

[SupportedOSPlatform("macos")]
internal sealed class MousePositionProvider : IMousePositionProvider
{
    public ScreenPosition? GetMousePosition()
    {
        try
        {
            // NSEvent.MouseLocation returns the current mouse location in screen coordinates
            // Note: macOS coordinate system has origin at bottom-left
            var location = NSEvent.CurrentMouseLocation;

            return new ScreenPosition
            {
                X = (int)location.X,
                Y = (int)location.Y
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
