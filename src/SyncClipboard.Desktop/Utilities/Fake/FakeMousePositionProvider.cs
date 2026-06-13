using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.Utilities.Fake;

internal sealed class FakeMousePositionProvider : IMousePositionProvider
{
    public ScreenPosition? GetMousePosition() => null;
}
