namespace SyncClipboard.Core.Commons.ConfigMigration;

public sealed class SyncClipboardConfigUpgradeException : Exception
{
    public SyncClipboardConfigUpgradeException(string message) : base(message)
    {
    }

    public SyncClipboardConfigUpgradeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
