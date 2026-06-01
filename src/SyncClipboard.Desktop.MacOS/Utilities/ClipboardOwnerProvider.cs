using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.MacOS.Utilities;

[SupportedOSPlatform("macos")]
internal sealed class ClipboardOwnerProvider : IClipboardOwnerProvider
{
    public ForegroundWindowInfo? GetClipboardOwner()
    {
        return null;
    }
}
