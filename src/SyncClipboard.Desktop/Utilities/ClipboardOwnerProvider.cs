using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class ClipboardOwnerProvider : IClipboardOwnerProvider
{
    public ForegroundWindowInfo? GetClipboardOwner()
    {
        return null;
    }
}
