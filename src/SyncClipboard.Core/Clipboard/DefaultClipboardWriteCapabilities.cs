using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Clipboard;

public sealed class DefaultClipboardWriteCapabilities : IClipboardWriteCapabilities
{
    public string SourceName => "Native";
    public bool SupportsMultipleFormats => true;
}
