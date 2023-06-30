using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Net.Http.Json;

namespace SyncClipboard.Core.Utilities
{
    public class Http : IHttp
    {
        public const string USER_AGENT = "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.4896.127 Mobile Safari/537.36";

        private string _proxy;
        private readonly UserConfig _userConfig;

        public Http(UserConfig userConfig)
        {
            _proxy = userConfig.Config.Program.Proxy;
            _userConfig = userConfig;
            userConfig.ConfigChanged += UserConfig_ConfigChanged;
        }

        private void UserConfig_ConfigChanged()
        {
            if (_proxy != _userConfig.Config.Program.Proxy)
            {
                _proxy = _userConfig.Config.Program.Proxy;
                HttpClientProxy.Dispose();
                _lazyHttpClientProxy = null;
            }
        }

        private static readonly Lazy<HttpClient> lazyHttpClient = new(
            () =>
            {
                HttpClient client = new(
                    new SocketsHttpHandler()
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(60),
                    });
                client.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
                return client;
            }
        );

        private static HttpClient HttpClient => lazyHttpClient.Value;
        private HttpClient HttpClientProxy => GetHttpClientProxy();

        private HttpClient? _lazyHttpClientProxy = null;

        private HttpClient GetHttpClientProxy()
        {
            if (_lazyHttpClientProxy is null)
            {
                _lazyHttpClientProxy = new(
                    new SocketsHttpHandler()
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(60),
                        Proxy = new System.Net.WebProxy(_proxy, true),
                        UseProxy = true
                    }
                );
                _lazyHttpClientProxy.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
            }
            return _lazyHttpClientProxy;
        }

        Task<Type?> IHttp.PostTextRecieveJson<Type>(string url, IEnumerable<KeyValuePair<string, string>>? list,
            bool useProxy) where Type : default
        {
            HttpClient client = useProxy ? HttpClientProxy : HttpClient;
            return client.PostTextRecieveJson<Type>(url, list);
        }

        Task IHttp.GetFile(string url, string localFilePath, CancellationToken? cancelToken, bool useProxy)
        {
            HttpClient client = useProxy ? HttpClientProxy : HttpClient;
            return client.GetFile(url, localFilePath, cancelToken);
        }

        Task IHttp.GetFile(string url, string localFilePath, IProgress<HttpDownloadProgress>? progress,
            CancellationToken? cancelToken, bool useProxy)
        {
            HttpClient client = useProxy ? HttpClientProxy : HttpClient;
            return client.GetFile(url, localFilePath, progress, cancelToken);
        }

        HttpClient IHttp.GetHttpClient(bool useProxy)
        {
            return useProxy ? HttpClientProxy : HttpClient;
        }
    }
}