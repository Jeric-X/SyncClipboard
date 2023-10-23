namespace SyncClipboard.Server
{
    public class StaticCredentialChecker : ICredentialChecker
    {
        private readonly string _userName;
        private readonly string _password;

        public StaticCredentialChecker(string name, string password)
        {
            _userName = name;
            _password = password;
        }

        public bool Check(string name, string password)
        {
            return name == _userName && password == _password;
        }
    }
}