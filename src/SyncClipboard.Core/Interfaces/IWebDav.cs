using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces
{
    public interface IWebDav
    {
        Task<string> GetText(string url, CancellationToken? cancelToken = null);
        Task PutText(string url, string text, CancellationToken? cancelToken = null);
        Task PutFile(string url, string localFilePath, CancellationToken? cancelToken = null);
        Task PutFile(string url, string localFilePath, IProgress<HttpDownloadProgress>? progress, CancellationToken? cancelToken = null);
        Task GetFile(string url, string localFilePath, CancellationToken? cancelToken = null);
        Task GetFile(string url, string localFilePath, IProgress<HttpDownloadProgress>? progress = null,
            CancellationToken? cancelToken = null);
        Task<bool> TestAlive(CancellationToken? cancelToken = null);
        Task Test(CancellationToken? cancelToken = null);
        Task<Type?> GetJson<Type>(string url, CancellationToken? cancelToken = null);
        Task PutJson<Type>(string url, Type jsonContent, CancellationToken? cancelToken = null);
        Task<bool> Exist(string url, CancellationToken? cancelToken = null);
        Task<bool> DirectoryExist(string url, CancellationToken? cancelToken = null);
        Task CreateDirectory(string url, CancellationToken? cancelToken = null);
        Task Delete(string url, CancellationToken? cancelToken = null);
        Task DirectoryDelete(string url, CancellationToken? cancelToken = null);
        public Task<List<WebDavNode>> GetFolderSubList(string url, CancellationToken? cancelToken = null);
    }
}