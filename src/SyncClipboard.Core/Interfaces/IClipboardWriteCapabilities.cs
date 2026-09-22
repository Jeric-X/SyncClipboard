namespace SyncClipboard.Core.Interfaces;

public interface IClipboardWriteCapabilities
{
    string SourceName { get; }
    bool SupportsMultipleFormats { get; }
}
