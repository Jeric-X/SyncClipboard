namespace SyncClipboard.Core.Clipboard;

public class LocalClipboard
{
    public static readonly SemaphoreSlim Semaphore = new(1, 1);
}
