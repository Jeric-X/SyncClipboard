namespace SyncClipboard.Core.Exceptions;

public class RemoteHistoryConflictException(string message, SyncClipboard.Server.Core.Models.HistoryRecordUpdateDto? server = null) : RemoteServerException(message)
{
    public SyncClipboard.Server.Core.Models.HistoryRecordUpdateDto? Server { get; } = server;
}
