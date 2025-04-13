namespace SyncClipboard.Server.Core.CredentialChecker
{
    public interface ICredentialChecker
    {
        public bool Check(string name, string password);
    }
}