namespace SyncClipboard.Core.Exceptions;

public class RemoteHistoryDataRejectedException(string message) : RemoteServerException(message)
{
}
