using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Web;
using System.Xml;

namespace SyncClipboard.Core.Utilities.Web
{
    abstract public class WebDavBase : IWebDav, IDisposable
    {
        private string USER_AGENT => AppConfig.AppStringId + AppConfig.AppVersion;

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        protected ILogger? Logger;
        protected abstract IAppConfig AppConfig { get; }

        public virtual uint Timeout { get; init; } = 300;
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

        private IWebProxy _proxy = new WebProxy();

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
            var newClient = CreateHttpClient();
            var oldClient = HttpClient;
            HttpClient = newClient;
            oldClient?.Dispose();
            SetAuthHeader();
        }

        public void SetProxy(IWebProxy proxy)
        {
            _proxy = proxy;
            // 如果 HttpClient 已经懒加载过，立即重建以应用新代理；
            // 如果还未创建，下次懒加载时自然会使用新 _proxy，无需主动重建。
            if (httpClient is not null)
            {
                ReInitHttpClient();
            }
        }

        protected virtual HttpClient CreateHttpClient()
        {
            var httpclientHandler = new HttpClientHandler
            {
                Proxy = _proxy // 显式应用代理
            };
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

        public async Task GetFile(string url, string localFilePath, CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            await HttpClient.GetFile(url, localFilePath, cancellationSource.Token);
        }

        public async Task GetFile(string url, string localFilePath, IProgress<HttpDownloadProgress>? progress = null,
            CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            await HttpClient.GetFile(url, localFilePath, progress, cancellationSource.Token);
        }

        public async Task PutFile(string url, string localFilePath, CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            using var fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var streamContent = new StreamContent(fileStream);
            using var response = await HttpClient.PutAsync(url, streamContent, cancellationSource.Token);
        }

        public async Task PutFile(string url, string localFilePath, IProgress<HttpDownloadProgress>? progress, CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            using var fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using HttpContent streamContent = progress is null
                ? new StreamContent(fileStream)
                : new ProgressableStreamContent(fileStream, progress, cancellationSource.Token);
            using var response = await HttpClient.PutAsync(url, streamContent, cancellationSource.Token);
        }

        public async Task<string> GetText(string url, CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            return await HttpClient.GetStringAsync(url, cancellationSource.Token);
        }

        public async Task PutText(string url, string text, CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            using var content = new StringContent(text);
            using var res = await HttpClient.PutAsync(url, content, cancellationSource.Token);
            res.EnsureSuccessStatusCode();
        }

        public async Task<Type?> GetJson<Type>(string url, CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            return await HttpClient.GetFromJsonAsync<Type>(
                url,
                SerializerOptions,
                cancellationSource.Token
            );
        }

        public async Task PutJson<Type>(string url, Type jsonContent, CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            using var content = JsonContent.Create(jsonContent, null, SerializerOptions);
            await content.LoadIntoBufferAsync(); // avoid chunked encoding
            using var response = await HttpClient.PutAsync(
                url,
                content,
                cancellationSource.Token
            );
        }

        private CancellationTokenSource CreateRequestCancellationSource(CancellationToken? cancelToken = null)
        {
            var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken ?? CancellationToken.None);
            cancellationSource.CancelAfter(TimeSpan.FromSeconds(Timeout));
            return cancellationSource;
        }

        public async Task<bool> Exist(string url, CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            using var requestMessage = new HttpRequestMessage(new HttpMethod("HEAD"), url);
            using var res = await HttpClient.SendAsync(requestMessage, cancellationSource.Token);
            return EnsureExist(res);
        }

        public async Task<bool> DirectoryExist(string url, CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            AdjustDirectoryUrl(ref url);
            using var requestMessage = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            requestMessage.Headers.Add("Depth", "1");
            using var res = await HttpClient.SendAsync(requestMessage, cancellationSource.Token);
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
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            using var requestMessage = new HttpRequestMessage(new HttpMethod("MKCOL"), url);
            using var res = await HttpClient.SendAsync(requestMessage, cancellationSource.Token);
            res.EnsureSuccessStatusCode();
        }

        public async Task Test(CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            using HttpRequestMessage requestMessage = new()
            {
                Method = new HttpMethod("PROPFIND")
            };
            requestMessage.Headers.Add("Depth", "1");

            using var res = await HttpClient.SendAsync(requestMessage, cancellationSource.Token);
            res.EnsureSuccessStatusCode();
        }

        public async Task<bool> TestAlive(CancellationToken? cancelToken = null)
        {
            try
            {
                await Test(cancelToken);
                return true;
            }
            catch (Exception ex)
            {
                Logger?.Write("[WebDAV] Test WebDav Failed, message = " + ex.Message);
                return false;
            }
        }

        public async Task Delete(string url, CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            using var res = await HttpClient.DeleteAsync(url, cancellationSource.Token);
            res.EnsureSuccessStatusCode();
        }

        public async Task DirectoryDelete(string url, CancellationToken? cancelToken = null)
        {
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            AdjustDirectoryUrl(ref url);
            using var res = await HttpClient.DeleteAsync(url, cancellationSource.Token);
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
            using var cancellationSource = CreateRequestCancellationSource(cancelToken);
            var token = cancellationSource.Token;
            using var requestMessage = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            requestMessage.Headers.Add("Depth", "1");
            using var res = await HttpClient.SendAsync(requestMessage, token);
            res.EnsureSuccessStatusCode();

            List<WebDavNode> list = [];

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(await res.Content.ReadAsStringAsync(token));

            XmlNamespaceManager namespaceManager = new(doc.NameTable);
            namespaceManager.AddNamespace("d", "DAV:");

            XmlNodeList? elemList = doc.SelectNodes("//d:response", namespaceManager);
            if (elemList is null || elemList.Count == 0)
            {
                return list;
            }
            foreach (XmlNode elem in elemList)
            {
                var hrefNode = elem.SelectSingleNode("d:href", namespaceManager);
                if (hrefNode is null)
                {
                    continue;
                }

                var fullPath = hrefNode.InnerText.Trim();
                if (string.IsNullOrEmpty(fullPath))
                {
                    continue;
                }

                var relativePath = fullPath.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                    ? fullPath[Prefix.Length..].Trim('/')
                    : new Uri(fullPath).AbsolutePath.Trim('/');

                var urlPath = url.Trim('/');
                string subName;
                if (relativePath.StartsWith(urlPath, StringComparison.OrdinalIgnoreCase))
                {
                    subName = relativePath[urlPath.Length..].Trim('/');
                }
                else
                {
                    subName = relativePath;
                }

                if (string.IsNullOrEmpty(subName))
                {
                    continue;
                }

                var isFolder = elem.SelectSingleNode("d:propstat/d:prop/d:resourcetype/d:collection", namespaceManager) is not null;
                list.Add(new(relativePath, HttpUtility.UrlDecode(subName), isFolder));
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
