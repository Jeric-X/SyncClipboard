using SyncClipboard.Core.Clipboard;

namespace SyncClipboard.Core.Interfaces;

public class ProfileChangedEventArgs : EventArgs
{
    public Profile NewProfile { get; init; } = new UnknownProfile();
}