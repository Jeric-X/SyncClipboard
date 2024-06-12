using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using System.Xml;

namespace SyncClipboard.Core.Utilities.Web
{
    abstract public class WebDavBase : IWebDav, IDisposable
    {
        private string USER_AGENT => AppConfig.AppStringId + AppConfig.AppVersion;

        protected ILogger? Logger;
        protected abstract IAppConfig AppConfig { get; }

        protected virtual uint Timeout => 300;
        protected abstract string User { get; }
        protected abstract string Token { get; }
        protected abstract string BaseAddress { get; }
        protected abstract bool TrustInsecureCertificate { get; }

        private string? _prefix;
        private string Prefix
        {
            get
            {
                _prefix ??= new Uri(BaseAddress).AbsolutePath.TrimEnd('/') + '/';
                return _prefix;
            }
        }

        private HttpClient? httpClient;
        private HttpClient HttpClient
        {
            get
            {
                if (httpClient is null)
                {
                    httpClient = CreateHttpClient();
                    SetAuthHeader();
                }
                return httpClient;
            }
            set
            {
                httpClient?.Dispose();
                httpClient = value;
            }
        }

        protected void ReInitHttpClient()
        {
            HttpClient = CreateHttpClient();
            SetAuthHeader();
        }

        private HttpClient CreateHttpClient()
        {
            var httpclientHandler = new HttpClientHandler();
            if (TrustInsecureCertificate)
            {
                httpclientHandler.ServerCertificateCustomValidationCallback = delegate { return true; };
            }

            var httpClient = new HttpClient(httpclientHandler)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);

            if (Uri.TryCreate(BaseAddress.TrimEnd('/', '\\') + '/', UriKind.Absolute, out Uri? uri))
            {
                httpClient.BaseAddress = uri;
            }

            return httpClient;
        }

        private void SetAuthHeader()
        {
            if (User is null && Token is null)
            {
                HttpClient.DefaultRequestHeaders.Authorization = null;
            }
            byte[] bytes = System.Text.Encoding.Default.GetBytes(User + ":" + Token);

            HttpClient.DefaultRequestHeaders.Authorization
                = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        }

        public Task GetFile(string url, string localFilePath, CancellationToken? cancelToken = null)
        {
            return HttpClient.GetFile(url, localFilePath, AdjustCancelToken(cancelToken));
        }

        public Task GetFile(string url, string localFilePath, IProgress<HttpDownloadProgress>? progress = null,
            CancellationToken? cancelToken = null)
        {
            return HttpClient.GetFile(url, localFilePath, progress, AdjustCancelToken(cancelToken));
        }

        public async Task PutFile(string url, string localFilePath, CancellationToken? cancelToken = null)
        {
            using var fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);
            using var streamContent = new StreamContent(fileStream);
            await HttpClient.PutAsync(url, streamContent, AdjustCancelToken(cancelToken));
        }

        public Task<string> GetText(string url, CancellationToken? cancelToken = null)
        {
            return HttpClient.GetStringAsync(url, AdjustCancelToken(cancelToken));
        }

        public async Task PutText(string url, string text, CancellationToken? cancelToken = null)
        {
            var res = await HttpClient.PutAsync(url, new StringContent(text), AdjustCancelToken(cancelToken));
            res.EnsureSuccessStatusCode();
        }

        public Task<Type?> GetJson<Type>(string url, CancellationToken? cancelToken = null)
        {
            return HttpClient.GetFromJsonAsync<Type>(
                url,
                new JsonSerializerOptions(JsonSerializerDefaults.General),
                AdjustCancelToken(cancelToken)
            );
        }

        public Task PutJson<Type>(string url, Type jsonContent, CancellationToken? cancelToken = null)
        {
            return HttpClient.PutAsJsonAsync(
                url,
                jsonContent,
                new JsonSerializerOptions(JsonSerializerDefaults.General),
                AdjustCancelToken(cancelToken)
            );
        }

        private CancellationToken AdjustCancelToken(CancellationToken? cancelToken = null)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancelToken ?? CancellationToken.None,
                new CancellationTokenSource(TimeSpan.FromSeconds(Timeout)).Token
            ).Token;
        }

        public async Task<bool> Exist(string url, CancellationToken? cancelToken = null)
        {
            var requestMessage = new HttpRequestMessage(new HttpMethod("HEAD"), url);
            var res = await HttpClient.SendAsync(requestMessage, AdjustCancelToken(cancelToken));
            return EnsureExist(res);
        }

        public async Task<bool> DirectoryExist(string url, CancellationToken? cancelToken = null)
        {
            AdjustDirectoryUrl(ref url);
            var requestMessage = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            requestMessage.Headers.Add("Depth", "1");
            var res = await HttpClient.SendAsync(requestMessage, AdjustCancelToken(cancelToken));
            return EnsureExist(res);
        }

        private static bool EnsureExist(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            response.EnsureSuccessStatusCode();
            return true;
        }

        public async Task CreateDirectory(string url, CancellationToken? cancelToken = null)
        {
            var requestMessage = new HttpRequestMessage(new HttpMethod("MKCOL"), url);
            var res = await HttpClient.SendAsync(requestMessage, AdjustCancelToken(cancelToken));
            res.EnsureSuccessStatusCode();
        }

        public async Task<bool> TestAlive(CancellationToken? cancelToken = null)
        {
            HttpRequestMessage requestMessage = new()
            {
                Method = new HttpMethod("PROPFIND")
            };
            requestMessage.Headers.Add("Depth", "1");

            try
            {
                var res = await HttpClient.SendAsync(requestMessage, AdjustCancelToken(cancelToken));
                res.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Logger?.Write("[WebDAV] Test WebDav Failed, message = " + ex.Message);
                return false;
            }

            Logger?.Write("Test ok ");
            return true;
        }

        public async Task Delete(string url, CancellationToken? cancelToken = null)
        {
            var res = await HttpClient.DeleteAsync(url, AdjustCancelToken(cancelToken));
            res.EnsureSuccessStatusCode();
        }

        public async Task DirectoryDelete(string url, CancellationToken? cancelToken = null)
        {
            AdjustDirectoryUrl(ref url);
            var res = await HttpClient.DeleteAsync(url, AdjustCancelToken(cancelToken));
            res.EnsureSuccessStatusCode();
        }

        private static void AdjustDirectoryUrl(ref string url)
        {
            if (!url.EndsWith('/'))
            {
                url += "/";
            }
        }

        public async Task<List<WebDavNode>> GetFolderSubList(string url, CancellationToken? cancelToken = null)
        {
            var token = AdjustCancelToken(cancelToken);
            var requestMessage = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            var res = await HttpClient.SendAsync(requestMessage, token);
            res.EnsureSuccessStatusCode();

            List<WebDavNode> list = new List<WebDavNode>();

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(await res.Content.ReadAsStringAsync(token));
            const string ns = "DAV:";
            XmlNodeList elemList = doc.GetElementsByTagName("response", ns);
            foreach (XmlNode elem in elemList)
            {
                var fullPath = elem["d:href"]!.InnerText;
                var relativePath = fullPath[Prefix.Length..];
                var subName = relativePath[url.Length..];
                subName = subName.Trim('/');
                if (!string.IsNullOrEmpty(subName))
                {
                    var isFolder = elem["propstat", ns]?["prop", ns]?["resourcetype", ns]?["collection", ns];
                    list.Add(new(relativePath, HttpUtility.UrlDecode(subName), isFolder is not null));
                }
            }
            return list;
        }

        ~WebDavBase() => Dispose();

        public void Dispose()
        {
            httpClient?.Dispose();
            httpClient = null;
            GC.SuppressFinalize(this);
        }
    }
}