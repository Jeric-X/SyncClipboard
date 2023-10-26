using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces
{
    public interface IHttp
    {
        public Task<Type?> PostTextRecieveJson<Type>(string url, IEnumerable<KeyValuePair<string, string>>? list = null,
            CancellationToken? cancelToken = null, bool useProxy = false);

        public Task GetFile(string url, string localFilePath, CancellationToken? cancelToken = null,
            bool useProxy = false);

        public Task GetFile(string url, string localFilePath, IProgress<HttpDownloadProgress>? progress,
            CancellationToken? cancelToken = null, bool useProxy = false);

        public HttpClient GetHttpClient(bool useProxy = false);
    }
}