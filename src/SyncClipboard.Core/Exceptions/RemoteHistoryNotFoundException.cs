namespace SyncClipboard.Core.Exceptions;

public class RemoteHistoryNotFoundException(string message) : RemoteServerException(message)
{
}
