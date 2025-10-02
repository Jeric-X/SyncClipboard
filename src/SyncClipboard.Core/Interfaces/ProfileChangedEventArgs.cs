using SyncClipboard.Core.Clipboard;

namespace SyncClipboard.Core.Interfaces;

public class ProfileChangedEventArgs : EventArgs
{
    public Profile NewProfile { get; init; } = new UnknownProfile();
    public Profile OldProfile { get; init; } = new UnknownProfile();
}