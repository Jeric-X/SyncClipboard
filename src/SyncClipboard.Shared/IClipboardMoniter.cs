namespace SyncClipboard.Shared;

public interface IClipboardMoniter
{
    event Action ClipboardChanged;
}
