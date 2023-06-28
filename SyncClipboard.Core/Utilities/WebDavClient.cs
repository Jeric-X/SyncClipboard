using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
#nullable enable

namespace SyncClipboard.Core.Utilities
{
    public class WebDavClient : IWebDav
    {
        private const string USER_AGENT = Env.SoftName + Env.VERSION;
        public int? IntervalTime { get; set; }
        public int? RetryTimes { get; set; }
        private int? _timeout;
        public int Timeout
        {
            get => _timeout ?? (int)httpClient.Timeout.TotalSeconds;
            set => _timeout = value;
        }

        private string? _user;
        public string? User
        {
            get => _user;
            set
            {
                _user = value;
                SetAuthHeader();
            }
        }

        private string? _token;
        public string? Token
        {
            get => _token;
            set
            {
                _token = value;
                SetAuthHeader();
            }
        }

        private readonly HttpClient httpClient;
        public WebDavClient(string url)
        {
            httpClient = new HttpClient()
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
            if (Uri.TryCreate(url?.TrimEnd('/', '\\') + '/', UriKind.Absolute, out Uri? uri))
            {
                httpClient.BaseAddress = uri;
            }

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
        }
        private void SetAuthHeader()
        {
            if ((User is null) && (Token is null))
            {
                httpClient.DefaultRequestHeaders.Authorization = null;
            }
            byte[] bytes = System.Text.Encoding.Default.GetBytes(User + ":" + Token);

            httpClient.DefaultRequestHeaders.Authorization
                = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        }

        public Task GetFile(string url, string localFilePath, CancellationToken? cancelToken = null)
        {
            return httpClient.GetFile(url, localFilePath, AdjustCancelToken(cancelToken));
        }

        public Task GetFile(string url, string localFilePath, IProgress<HttpDownloadProgress>? progress = null,
            CancellationToken? cancelToken = null)
        {
            return httpClient.GetFile(url, localFilePath, progress, AdjustCancelToken(cancelToken));
        }

        public async Task PutFile(string url, string localFilePath, CancellationToken? cancelToken = null)
        {
            using var fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);
            using var streamContent = new StreamContent(fileStream);
            await httpClient.PutAsync(url, streamContent, AdjustCancelToken(cancelToken));
        }

        public Task<string> GetText(string url, CancellationToken? cancelToken = null)
        {
            return httpClient.GetStringAsync(url, AdjustCancelToken(cancelToken));
        }

        public async Task PutText(string url, string text, CancellationToken? cancelToken = null)
        {
            var res = await httpClient.PutAsync(url, new StringContent(text), AdjustCancelToken(cancelToken));
            res.EnsureSuccessStatusCode();
        }

        public Task<Type?> GetJson<Type>(string url, CancellationToken? cancelToken = null)
        {
            return httpClient.GetFromJsonAsync<Type>(
                url,
                new JsonSerializerOptions(JsonSerializerDefaults.General),
                AdjustCancelToken(cancelToken)
            );
        }

        public Task PutJson<Type>(string url, Type jsonContent, CancellationToken? cancelToken = null)
        {
            return httpClient.PutAsJsonAsync<Type>(
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
            var res = await httpClient.SendAsync(requestMessage, AdjustCancelToken(cancelToken));
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            res.EnsureSuccessStatusCode();
            return true;
        }

        public async Task CreateDirectory(string url, CancellationToken? cancelToken = null)
        {
            var requestMessage = new HttpRequestMessage(new HttpMethod("MKCOL"), url);
            var res = await httpClient.SendAsync(requestMessage, AdjustCancelToken(cancelToken));
            res.EnsureSuccessStatusCode();
        }

        public async Task<bool> TestAlive(CancellationToken? cancelToken = null)
        {
            HttpRequestMessage requestMessage = new()
            {
                Method = HttpMethod.Head
            };

            try
            {
                await httpClient.SendAsync(requestMessage, AdjustCancelToken(cancelToken));
            }
            catch (Exception ex)
            {
                Log.Write("[WebDAV] Test WebDav Failed, message = " + ex.Message);
                return false;
            }

            Log.Write("Test ok ");
            return true;
        }

        public async Task Delete(string url, CancellationToken? cancelToken = null)
        {
            var res = await httpClient.DeleteAsync(url, AdjustCancelToken(cancelToken));
            res.EnsureSuccessStatusCode();
        }
    }
}