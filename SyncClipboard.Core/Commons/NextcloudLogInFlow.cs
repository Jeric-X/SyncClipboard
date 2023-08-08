using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Commons
{
    public class NextcloudLogInFlow
    {
        #region nextcloud official response defination

        private class FirstResponseJson
        {
            public class CPoll
            {
                public string Token { get; set; } = "";
                public string Endpoint { get; set; } = "";
            }

            public CPoll Poll { get; set; } = new();
            public string Login { get; set; } = "";
        }

        private class SecondResponse
        {
            public string Server { get; set; } = "";
            public string LoginName { get; set; } = "";
            public string AppPassword { get; set; } = "";
        }

        #endregion nextcloud official response defination

        private const string VERIFICATION_URL = "/index.php/login/v2";
        private const string WEBDAV_URL = "remote.php/dav/files";
        public const int VERIFICATION_LIMITED_TIME = 60000;
        private const int INTERVAL_TIME = 1000;

        private readonly string _server;
        private FirstResponseJson? _firstResponse;
        private readonly IHttp _httpClient;

        public WebDavCredential? Result { get; private set; }
        public int VerificationLimitedTime { get; set; } = VERIFICATION_LIMITED_TIME;

        public NextcloudLogInFlow(string server, IHttp httpClient)
        {
            _server = server;
            _httpClient = httpClient;
        }

        public async Task<string> GetUserLoginUrl()
        {
            var loginUrl = _server + VERIFICATION_URL;
            var firstResponse = await _httpClient.PostTextRecieveJson<FirstResponseJson>(loginUrl);

            ArgumentNullException.ThrowIfNull(firstResponse);
            _firstResponse = firstResponse;
            return _firstResponse.Login;
        }

        public async Task<WebDavCredential> WaitUserLogin(CancellationToken? cancellationToken = null)
        {
            if (Result is not null) return Result;

            var secondResponse = await GetSecondResponse(cancellationToken);
            ArgumentNullException.ThrowIfNull(secondResponse);

            var credential = new WebDavCredential
            {
                Username = secondResponse.LoginName,
                Password = secondResponse.AppPassword,
                Url = $"{secondResponse.Server}/{WEBDAV_URL}/{secondResponse.LoginName}"
            };
            Result = credential;
            return Result;
        }

        private async Task<SecondResponse?> GetSecondResponse(CancellationToken? cancellationToken)
        {
            if (_firstResponse is null)
            {
                throw new Exception("It is not allowed to call this method right now.");
            }

            var token = CancellationTokenSource.CreateLinkedTokenSource(
               cancellationToken ?? CancellationToken.None,
               new CancellationTokenSource(TimeSpan.FromMilliseconds(VerificationLimitedTime)).Token
            ).Token;

            var url = _firstResponse.Poll.Endpoint;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    KeyValuePair<string, string>[] list = { KeyValuePair.Create("token", _firstResponse.Poll.Token) };
                    return await _httpClient.PostTextRecieveJson<SecondResponse>(url, list, token);
                }
                catch
                {
                    await Task.Delay(INTERVAL_TIME, token);
                }
            }
        }
    }
}