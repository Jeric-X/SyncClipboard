namespace SyncClipboard.Server
{
    public interface ICredentialChecker
    {
        public bool Check(string name, string password);
    }
}