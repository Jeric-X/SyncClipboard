namespace SyncClipboard.Core.Exceptions;

public class RemoteServerException : Exception
{
    public RemoteServerException(string message) : base(message)
    {
    }

    public RemoteServerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}