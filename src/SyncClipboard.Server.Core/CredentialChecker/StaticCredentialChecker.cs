namespace SyncClipboard.Server.Core.CredentialChecker
{
    public class StaticCredentialChecker(string name, string password) : ICredentialChecker
    {
        private readonly string _userName = name;
        private readonly string _password = password;

        public bool Check(string name, string password)
        {
            return name == _userName && password == _password;
        }
    }
}