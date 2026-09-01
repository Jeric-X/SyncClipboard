namespace SyncClipboard.Shared.Profiles;

public class LocalProfileDataUnavailableException : IOException
{
    public LocalProfileDataUnavailableException(string message) : base(message)
    {
    }

    public LocalProfileDataUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
