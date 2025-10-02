namespace SyncClipboard.Core.Interfaces;

public class PollStatusEventArgs : EventArgs
{
    public PollStatus Status { get; init; }
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
}