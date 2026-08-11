namespace SyncClipboard.Core.Clipboard;

public static class NativeClipboardAccess
{
    // Native clipboard APIs may raise change events while a write is still in progress.
    // Serialize reads and writes to prevent those events from re-entering the clipboard.
    public static SemaphoreSlim Semaphore { get; } = new(1, 1);

    public static async Task<ScopeGuard> AcquireAsync(CancellationToken cancellationToken)
    {
        await Semaphore.WaitAsync(cancellationToken);
        return new ScopeGuard(() => Semaphore.Release());
    }
}
