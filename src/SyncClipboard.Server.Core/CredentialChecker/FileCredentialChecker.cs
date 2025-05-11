namespace SyncClipboard.Server.Core.CredentialChecker
{
    public class FileCredentialChecker(IConfiguration configuration) : ICredentialChecker
    {
        private const string DEFAULT_USERNAME = "admin";
        private const string DEFAULT_PASSWORD = "admin";

        private readonly IConfiguration configuration = configuration;

        public bool Check(string name, string password)
        {
            var configUserName = configuration["AppSettings:UserName"] ?? DEFAULT_USERNAME;
            var configPassword = configuration["AppSettings:Password"] ?? DEFAULT_PASSWORD;
            return name == configUserName && password == configPassword;
        }
    }
}