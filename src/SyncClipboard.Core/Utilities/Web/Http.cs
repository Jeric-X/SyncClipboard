using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Web
{
    public class Http : IHttp
    {
        public const string USER_AGENT = "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.4896.127 Mobile Safari/537.36";

        private string _proxy;

        public Http(ConfigManager configManager)
        {
            configManager.ListenConfig<ProgramConfig>(ConfigChanged);
            var programConfig = configManager.GetConfig<ProgramConfig>();

            _proxy = programConfig.Proxy;
        }

        private void ConfigChanged(object? config)
        {
            var newConfig = config as ProgramConfig ?? new();
            if (newConfig.Proxy != _proxy)
            {
                _proxy = newConfig.Proxy;
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
            CancellationToken? cancelToken, bool useProxy) where Type : default
        {
            HttpClient client = useProxy ? HttpClientProxy : HttpClient;
            return client.PostTextRecieveJson<Type>(url, list, cancelToken);
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