using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Core.Exceptions;

public class RemoteHistoryConflictException(string message, HistoryRecordUpdateDto? server = null) : RemoteServerException(message)
{
    public HistoryRecordUpdateDto? ServerRecord { get; } = server;
}
