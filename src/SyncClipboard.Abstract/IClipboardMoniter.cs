namespace SyncClipboard.Abstract;

public interface IClipboardMoniter
{
    event Action ClipboardChanged;
}
