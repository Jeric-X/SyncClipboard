namespace SyncClipboard.Core.Commons.ConfigMigration;

public sealed class SyncClipboardConfigUpgradeException : Exception
{
    public string? RecoverableSectionKey { get; }

    public SyncClipboardConfigUpgradeException(string message) : base(message)
    {
    }

    public SyncClipboardConfigUpgradeException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public SyncClipboardConfigUpgradeException(string message, string recoverableSectionKey) : base(message)
    {
        RecoverableSectionKey = recoverableSectionKey;
    }

    public SyncClipboardConfigUpgradeException(
        string message,
        string recoverableSectionKey,
        Exception innerException) : base(message, innerException)
    {
        RecoverableSectionKey = recoverableSectionKey;
    }
}
