using System;
using System.Threading;
using System.Threading.Tasks;
#nullable enable

namespace SyncClipboard.Utility.Web
{
    public interface IWebDav
    {
        int? IntervalTime { get; set; }
        int? RetryTimes { get; set; }
        int Timeout { get; set; }
        string? User { get; set; }
        string? Token { get; set; }

        Task<string> GetText(string url, CancellationToken? cancelToken = null);
        Task PutText(string url, string text, CancellationToken? cancelToken = null);
        Task PutFile(string url, string localFilePath, CancellationToken? cancelToken = null);
        Task GetFile(string url, string localFilePath, CancellationToken? cancelToken = null);
        Task GetFile(string url, string localFilePath, IProgress<HttpDownloadProgress>? progress = null,
            CancellationToken? cancelToken = null);
        Task<bool> TestAlive(CancellationToken? cancelToken = null);
        Task<Type?> GetJson<Type>(string url, CancellationToken? cancelToken = null);
        Task PutJson<Type>(string url, Type jsonContent, CancellationToken? cancelToken = null);
        Task<bool> Exist(string url, CancellationToken? cancelToken = null);
        Task CreateDirectory(string url, CancellationToken? cancelToken = null);
    }
}