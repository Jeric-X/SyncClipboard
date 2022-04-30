using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.IO;
#nullable enable

namespace SyncClipboard.Utility.Web
{
    public static class Http
    {
        public const string USER_AGENT = "SyncClipboard " + Env.VERSION;
        private const int BUFFER_SIZE = 102400;
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
        public static HttpClient HttpClient => lazyHttpClient.Value;

        public static async Task<Type?> PostTextRecieveJson<Type>(string url,
            IEnumerable<KeyValuePair<string, string>>? list = null)
        {
            list ??= Array.Empty<KeyValuePair<string, string>>();
            var res = await HttpClient.PostAsync(url, new FormUrlEncodedContent(list));
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<Type>();
        }

        public static async Task GetFile(this HttpClient httpClient,
            string url, string localFilePath, CancellationToken? cancelToken = null)
        {
            using var instream = await httpClient.GetStreamAsync(url, cancelToken ?? CancellationToken.None);
            using var fileStrem = new FileStream(localFilePath, FileMode.Create);
            await instream.CopyToAsync(fileStrem, cancelToken ?? CancellationToken.None);
        }

        public static async Task GetFile(this HttpClient httpClient, string url, string localFilePath,
            IProgress<HttpDownloadProgress>? progress, CancellationToken? cancellationToken = null)
        {
            var cancelToken = cancellationToken ?? CancellationToken.None;
            using var responseMessage = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancelToken);
            responseMessage.EnsureSuccessStatusCode();

            var content = responseMessage.Content;
            var contentLength = content.Headers.ContentLength;
            using var responseStream = await content.ReadAsStreamAsync(cancelToken);

            var downloadProgress = new HttpDownloadProgress();
            if (contentLength.HasValue)
            {
                downloadProgress.TotalBytesToReceive = (ulong)contentLength.Value;
            }
            progress?.Report(downloadProgress);

            var buffer = new byte[BUFFER_SIZE];
            int bytesRead;
            var bytes = new List<byte>();

            using var fileStrem = new FileStream(localFilePath, FileMode.Create);

            while ((bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, BUFFER_SIZE), cancelToken)) > 0)
            {
                await fileStrem.WriteAsync(buffer.AsMemory(0, bytesRead), cancelToken);
                downloadProgress.BytesReceived += (ulong)bytesRead;
                progress?.Report(downloadProgress);
            }
            downloadProgress.End = true;
            progress?.Report(downloadProgress);
        }
    }
}