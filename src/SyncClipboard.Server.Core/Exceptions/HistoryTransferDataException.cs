namespace SyncClipboard.Server.Core.Exceptions;

public class HistoryTransferDataException : Exception
{
    public HistoryTransferDataException(string message) : base(message)
    {
    }

    public HistoryTransferDataException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
