using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.Utilities.Fake;

internal sealed class FakeCaretPositionProvider : ICaretPositionProvider
{
    public ScreenPosition? GetCaretPosition() => null;
}
