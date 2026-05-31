using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.MacOS.Utilities;

[SupportedOSPlatform("macos")]
internal sealed class MousePositionProvider : IMousePositionProvider
{
    public ScreenPosition GetMousePosition()
    {
        try
        {
            return ScreenPosition.Invalid;
        }
        catch
        {
            return ScreenPosition.Invalid;
        }
    }
}
