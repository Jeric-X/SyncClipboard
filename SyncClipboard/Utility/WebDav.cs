using System;
using System.Threading.Tasks;

namespace SyncClipboard.Utility
{
    public interface IWebDav
    {
        int IntervalTime { get; set; }
        int RetryTimes { get; set; }
        int TimeOut { get; set; }

        string GetText(string remotefile);
        void PutText(string remotefile, string text);
        void PutFile(string remotefile, string localFilePath);
        void GetFile(string remotefile, string localFilePath);
        Task<bool> TestAliveAsync();
    }

    public class WebDav : IWebDav
    {
        # region 私有成员

        private string _url;
        private string _username;
        private string _password;
        private string _authHeader;

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
                string resault = await LoopAsync<string>(
                    () =>
                    {
                        return HttpWeb.Operate(_url, "PROPFIND", _authHeader);
                    }
                );
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
            return HttpWeb.GetText(FullUrl(file), _authHeader);
        }

        public void PutText(string file, string text)
        {
            HttpWeb.PutText(FullUrl(file), text, _authHeader);
        }


        public void PutFile(string remotefile, string localFilePath)
        {
            HttpWeb.PutFile(FullUrl(remotefile), localFilePath, _authHeader);
        }

        public void GetFile(string remotefile, string localFilePath)
        {
            HttpWeb.GetFile(FullUrl(remotefile), localFilePath, _authHeader);
        }

        # region 内部工具函数

        private T Loop<T>(Func<T> func)
        {
            for (int i = 1; i <= RetryTimes; i++)
            {
                try
                {
                    return func();
                }
                catch (Exception ex)
                {
                    if (i == RetryTimes)
                    {
                        throw ex;
                    }
                }
                Task.Delay(IntervalTime);
            }
            return default(T);
        }

        private async Task<T> LoopAsync<T>(Func<T> func)
        {
            for (int i = 1; i <= RetryTimes; i++)
            {
                try
                {
                    return await RunAsync<T>(func);
                }
                catch (Exception ex)
                {
                    if (i == RetryTimes)
                    {
                        throw ex;
                    }
                }
                await Task.Delay(IntervalTime);
            }
            return default(T);
        }

        private async Task<T> RunAsync<T>(Func<T> t)
        {
            return await Task.Run(() => t());
        }

        private string FullUrl(string relativeUrl)
        {
            return string.Join("/", _url, relativeUrl);
        }

        # endregion
    }
}
