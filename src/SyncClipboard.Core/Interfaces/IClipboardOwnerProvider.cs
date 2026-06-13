using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IClipboardOwnerProvider
{
    ForegroundWindowInfo? GetClipboardOwner();
}
