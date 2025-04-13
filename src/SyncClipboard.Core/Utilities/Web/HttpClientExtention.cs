using SyncClipboard.Core.Models;
using System.Net.Http.Json;

namespace SyncClipboard.Core.Utilities.Web
{
    public static class HttpClientExtention
    {
        private const int BUFFER_SIZE = 102400;

        public static async Task<Type?> PostTextRecieveJson<Type>(this HttpClient httpClient, string url,
            IEnumerable<KeyValuePair<string, string>>? list = null, CancellationToken? cancellationToken = null)
        {
            list ??= [];
            var res = await httpClient.PostAsync(url, new FormUrlEncodedContent(list), cancellationToken ?? CancellationToken.None);
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