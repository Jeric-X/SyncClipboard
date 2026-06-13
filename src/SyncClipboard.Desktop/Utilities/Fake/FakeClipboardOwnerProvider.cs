using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.Utilities.Fake;

internal sealed class FakeClipboardOwnerProvider : IClipboardOwnerProvider
{
    public ForegroundWindowInfo? GetClipboardOwner() => null;
}
