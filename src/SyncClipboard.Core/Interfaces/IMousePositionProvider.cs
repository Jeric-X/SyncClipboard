using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IMousePositionProvider
{
    ScreenPosition GetMousePosition();
}
