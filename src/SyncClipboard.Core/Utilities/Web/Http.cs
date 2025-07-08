using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Diagnostics.CodeAnalysis;

namespace SyncClipboard.Core.Utilities.Web
{
    public class Http : IHttp
    {
        public const string USER_AGENT = "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.4896.127 Mobile Safari/537.36";

        public Http()
        {
            CreateClient();
            ProxyManager.GlobalProxyChanged += CreateClient;
        }

        [MemberNotNull(nameof(HttpClient))]
        private void CreateClient()
        {
            var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(120),
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);

            var old = HttpClient;
            HttpClient = httpClient;
            old?.Dispose();
        }

        private HttpClient HttpClient;

        Task<Type?> IHttp.PostTextRecieveJson<Type>(string url, IEnumerable<KeyValuePair<string, string>>? list,
            CancellationToken? cancelToken) where Type : default
        {
            HttpClient client = HttpClient;
            return client.PostTextRecieveJson<Type>(url, list, cancelToken);
        }

        Task IHttp.GetFile(string url, string localFilePath, CancellationToken? cancelToken)
        {
            HttpClient client = HttpClient;
            return client.GetFile(url, localFilePath, cancelToken);
        }

        Task IHttp.GetFile(string url, string localFilePath, IProgress<HttpDownloadProgress>? progress,
            CancellationToken? cancelToken)
        {
            HttpClient client = HttpClient;
            return client.GetFile(url, localFilePath, progress, cancelToken);
        }

        HttpClient IHttp.GetHttpClient()
        {
            return HttpClient;
        }
    }
}