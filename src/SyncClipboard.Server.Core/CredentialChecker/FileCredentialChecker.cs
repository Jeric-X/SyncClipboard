namespace SyncClipboard.Server
{
    public class FileCredentialChecker : ICredentialChecker
    {
        private const string DEFAULT_USERNAME = "admin";
        private const string DEFAULT_PASSWORD = "admin";
        readonly IConfiguration _configuration;

        public FileCredentialChecker(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public bool Check(string name, string password)
        {
            var configUserName = _configuration["AppSettings:UserName"] ?? DEFAULT_USERNAME;
            var configPassword = _configuration["AppSettings:Password"] ?? DEFAULT_PASSWORD;
            return name == configUserName && password == configPassword;
        }
    }
}