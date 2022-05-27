using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using SyncClipboard.Module;
#nullable enable

namespace SyncClipboard.Utility.Web
{
    public static class Http
    {
        public const string USER_AGENT = "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.4896.127 Mobile Safari/537.36";
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

        private static readonly Lazy<HttpClient> lazyHttpClientProxy = new(
            () =>
            {
                HttpClient client = new(
                    new SocketsHttpHandler()
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(60),
                        Proxy = new System.Net.WebProxy(UserConfig.Config.Program.Proxy, true),
                        UseProxy = true
                    }
                );
                client.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
                return client;
            }
        );
        public static HttpClient HttpClientProxy => lazyHttpClientProxy.Value;

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

            var downloadProgress = new HttpDownloadProgress
            {
                TotalBytesToReceive = (ulong?)responseMessage.Content.Headers.ContentLength
            };
            progress?.Report(downloadProgress);

            var buffer = new byte[BUFFER_SIZE];
            int bytesRead;

            using var responseStream = await responseMessage.Content.ReadAsStreamAsync(cancelToken);
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