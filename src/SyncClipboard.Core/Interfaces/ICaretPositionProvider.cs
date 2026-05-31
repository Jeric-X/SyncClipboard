using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface ICaretPositionProvider
{
    ScreenPosition GetCaretPosition();
}
