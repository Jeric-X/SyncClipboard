namespace SyncClipboard.Core.Clipboard;

/// <summary>Identifies a clipboard notification without cancelling an otherwise successful read.</summary>
public readonly struct ClipboardChangeToken
{
    private readonly ClipboardChangeTokenSource? _source;
    private readonly long _version;

    internal ClipboardChangeToken(ClipboardChangeTokenSource source, long version)
    {
        _source = source;
        _version = version;
    }

    /// <summary>Whether a newer notification has arrived. A default token is never superseded.</summary>
    public bool IsSuperseded => _source?.IsSuperseded(_version) ?? false;
}
