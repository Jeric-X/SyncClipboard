using System;
using System.Net;
using System.Threading.Tasks;

namespace SyncClipboard.Utility
{
    public interface IWebDav
    {
        int IntervalTime { get; set; }
        int RetryTimes { get; set; }
        int TimeOut { get; set; }

        string GetText(string remotefile);
        Task<string> GetTextAsync(string remotefile, int retryTimes, int intervalTime);
        void PutText(string remotefile, string text);
        Task PutTextAsync(string remotefile, string text, int retryTimes, int intervalTime);
        void PutFile(string remotefile, string localFilePath);
        void GetFile(string remotefile, string localFilePath);
        Task<bool> TestAliveAsync();
    }

    public class WebDav : IWebDav
    {
        # region 私有成员

        private readonly string _url;
        private readonly string _username;
        private readonly string _password;
        private readonly string _authHeader;
        private CookieCollection _cookies;

        # endregion

        public int IntervalTime { get; set; } = UserConfig.Config.Program.IntervalTime;
        public int RetryTimes { get; set; } = UserConfig.Config.Program.RetryTimes;
        public int TimeOut { get; set; } = UserConfig.Config.Program.TimeOut;

        # region 构造函数
        public WebDav(string url, string username, string password)
        {
            _url = url;
            _username = username;
            _password = password;
            _authHeader = FormatHttpAuthHeader(_username, _password);
        }

        private static string FormatHttpAuthHeader(string username, string password)
        {
            byte[] bytes = System.Text.Encoding.Default.GetBytes(username + ":" + password);
            return "Authorization: Basic " + System.Convert.ToBase64String(bytes);
        }
        # endregion

        public async Task<bool> TestAliveAsync()
        {
            try
            {
                _cookies = await LoopAsync<CookieCollection>(
                    () => HttpWeb.GetCookie(_url, "HEAD", _authHeader)
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Write("[WebDAV] Test WebDav Failed, message = " + ex.Message);
                return false;
            }

            return true;
        }

        public string GetText(string file)
        {
            return HttpWeb.GetText(FullUrl(file), _authHeader, _cookies);
        }

        public async Task<string> GetTextAsync(string remotefile, int retryTimes, int intervalTime)
        {
            return await LoopAsync<string>(
                () => HttpWeb.GetText(FullUrl(remotefile), _authHeader, _cookies),
                retryTimes,
                intervalTime
            ).ConfigureAwait(false);
        }

        public void PutText(string file, string text)
        {
            HttpWeb.PutText(FullUrl(file), text, _authHeader, _cookies);
        }

        public async Task PutTextAsync(string file, string text, int retryTimes, int intervalTime)
        {
            await LoopAsync(
                () => HttpWeb.PutText(FullUrl(file), text, _authHeader, _cookies),
                retryTimes,
                intervalTime
            ).ConfigureAwait(false);
        }

        public void PutFile(string remotefile, string localFilePath)
        {
            HttpWeb.PutFile(FullUrl(remotefile), localFilePath, _authHeader, _cookies);
        }

        public void GetFile(string remotefile, string localFilePath)
        {
            HttpWeb.GetFile(FullUrl(remotefile), localFilePath, _authHeader, _cookies);
        }

        # region 内部工具函数

        private async Task<T> LoopAsync<T>(Func<T> func)
        {
            return await LoopAsyncDetail<T>(func, RetryTimes, IntervalTime).ConfigureAwait(false);
        }

        private async Task<T> LoopAsync<T>(Func<T> func, int retryTimes, int intervalTime)
        {
            return await LoopAsyncDetail<T>(func, retryTimes, intervalTime).ConfigureAwait(false);
        }

        private async Task LoopAsync(Action action, int retryTimes, int intervalTime)
        {
            await LoopAsyncDetail(action, retryTimes, intervalTime).ConfigureAwait(false);
        }

        private async Task<T> LoopAsyncDetail<T>(Func<T> func, int retryTimes, int intervalTime)
        {
            for (int i = 0; i <= retryTimes; i++)
            {
                try
                {
                    return await RunAsync<T>(func).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    if (i == retryTimes)
                    {
                        throw;
                    }
                }
                await Task.Delay(intervalTime).ConfigureAwait(false);
            }
            return default;
        }

        private async Task LoopAsyncDetail(Action action, int retryTimes, int intervalTime)
        {
            for (int i = 0; i <= retryTimes; i++)
            {
                try
                {
                    await RunAsync(action).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (i == retryTimes)
                    {
                        throw ex;
                    }
                }
                await Task.Delay(intervalTime).ConfigureAwait(false);
            }
        }

        private async Task<T> RunAsync<T>(Func<T> t)
        {
            return await Task.Run(() => t()).ConfigureAwait(false);
        }

        private async Task RunAsync(Action action)
        {
            await Task.Run(() => action()).ConfigureAwait(false);
        }

        private string FullUrl(string relativeUrl)
        {
            return string.Join("/", _url, relativeUrl);
        }

        # endregion
    }
}
