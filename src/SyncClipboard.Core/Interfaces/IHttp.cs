using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces
{
    public interface IHttp
    {
        public Task<Type?> PostTextRecieveJson<Type>(string url, IEnumerable<KeyValuePair<string, string>>? list = null,
            CancellationToken? cancelToken = null);

        public Task GetFile(string url, string localFilePath, CancellationToken? cancelToken = null);

        public Task GetFile(string url, string localFilePath, IProgress<HttpDownloadProgress>? progress,
            CancellationToken? cancelToken = null);

        public HttpClient GetHttpClient();
    }
}