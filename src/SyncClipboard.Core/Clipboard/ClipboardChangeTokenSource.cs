namespace SyncClipboard.Core.Clipboard;

/// <summary>Tracks notification arrival independently of the native clipboard read lock.</summary>
public sealed class ClipboardChangeTokenSource
{
    private long _version;

    /// <summary>Marks earlier tokens as superseded and returns the token for this notification.</summary>
    public ClipboardChangeToken NotifyChange() => new(this, Interlocked.Increment(ref _version));

    internal bool IsSuperseded(long version) => version != Volatile.Read(ref _version);
}
