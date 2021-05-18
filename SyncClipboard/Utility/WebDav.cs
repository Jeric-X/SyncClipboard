using System;
using System.Net;
using System.Threading.Tasks;

namespace SyncClipboard.Utility
{
    public interface IWebDav
    {
        int IntervalTime { get; set; }
        int RetryTimes { get; set; }
        int Timeout { get; set; }

        string GetText(string remotefile);
        Task<string> GetTextAsync(string remotefile, int? retryTimes = null, int? intervalTime = null);
        void PutText(string remotefile, string text);
        Task PutTextAsync(string remotefile, string text, int? retryTimes = null, int? intervalTime = null);
        void PutFile(string remotefile, string localFilePath);
        void GetFile(string remotefile, string localFilePath);
        Task GetFileAsync(string remotefile, string localFilePath, int? retryTimes = null, int? intervalTime = null);
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
        private HttpPara _httpPara;

        # endregion

        public int IntervalTime { get; set; }
        public int RetryTimes { get; set; }

        private int _timeout;
        public int Timeout {
            get => _timeout;
            set
            {
                _timeout = value;
                _httpPara.Timeout = value;
            }
        }

        # region 构造函数
        public WebDav(string url, string username, string password, int intervalTime, int retryTimes, int timeout)
        {
            _url = url;
            _username = username;
            _password = password;
            _authHeader = FormatHttpAuthHeader(_username, _password);
            _httpPara = new HttpPara { AuthHeader = _authHeader };

            IntervalTime = intervalTime;
            RetryTimes = retryTimes;
            Timeout = timeout;

            TestAliveAsync().ContinueWith((task) =>
            {
                if (task.Result)
                {
                    _httpPara.Cookies = _cookies;
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        private static string FormatHttpAuthHeader(string username, string password)
        {
            byte[] bytes = System.Text.Encoding.Default.GetBytes(username + ":" + password);
            return "Authorization: Basic " + Convert.ToBase64String(bytes);
        }
        # endregion

        public async Task<bool> TestAliveAsync()
        {
            try
            {
                _cookies = await LoopAsync(
                    () => HttpWeb.GetCookie(_url, new HttpPara{ AuthHeader = _authHeader })
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
            return HttpWeb.GetText(FullUrl(file), _httpPara);
        }

        public async Task<string> GetTextAsync(string remotefile, int? retryTimes, int? intervalTime)
        {
            return await LoopAsync(
                () => HttpWeb.GetText(FullUrl(remotefile), _httpPara),
                retryTimes,
                intervalTime
            ).ConfigureAwait(false);
        }

        public void PutText(string file, string text)
        {
            HttpWeb.PutText(FullUrl(file), _httpPara, text);
        }

        public async Task PutTextAsync(string file, string text, int? retryTimes, int? intervalTime)
        {
            await LoopAsync(
                () => HttpWeb.PutText(FullUrl(file), _httpPara, text),
                retryTimes,
                intervalTime
            ).ConfigureAwait(false);
        }

        public void PutFile(string remotefile, string localFilePath)
        {
            HttpWeb.PutFile(FullUrl(remotefile), _httpPara, localFilePath);
        }

        public async Task PutFileAsync(string remotefile, string localFilePath, int retryTimes, int intervalTime)
        {
            await LoopAsync(
                () => HttpWeb.PutFile(FullUrl(remotefile), _httpPara, localFilePath),
                retryTimes,
                intervalTime
            ).ConfigureAwait(false);
        }

        public void GetFile(string remotefile, string localFilePath)
        {
            HttpWeb.GetFile(FullUrl(remotefile), _httpPara, localFilePath);
        }

        public async Task GetFileAsync(string remotefile, string localFilePath, int? retryTimes, int? intervalTime)
        {
            await LoopAsync(
                () => HttpWeb.GetFile(FullUrl(remotefile), _httpPara, localFilePath),
                retryTimes,
                intervalTime
            ).ConfigureAwait(false);
        }

        # region 内部工具函数

        private async Task<T> LoopAsync<T>(Func<T> func, int? retryTimes = null, int? intervalTime = null)
        {
            AdjustPara(ref retryTimes, ref intervalTime);
            return await LoopAsyncDetail(func, (int)retryTimes, (int)intervalTime).ConfigureAwait(false);
        }

        private async Task LoopAsync(Action action, int? retryTimes = null, int? intervalTime = null)
        {
            AdjustPara(ref retryTimes, ref intervalTime);
            await LoopAsyncDetail(action, (int)retryTimes, (int)intervalTime).ConfigureAwait(false);
        }

        private void AdjustPara(ref int? retryTimes, ref int? intervalTime)
        {
            if (retryTimes is null)
            {
                retryTimes = RetryTimes;
            }

            if (intervalTime is null)
            {
                intervalTime = IntervalTime;
            }
        }

        private async Task<T> LoopAsyncDetail<T>(Func<T> func, int retryTimes, int intervalTime)
        {
            for (int i = 0; i <= retryTimes; i++)
            {
                try
                {
                    return await RunAsync(func).ConfigureAwait(false);
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
                catch
                {
                    if (i == retryTimes)
                    {
                        throw;
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
