using System;
using System.Threading.Tasks;

namespace SyncClipboard.Utility
{
    public class WebDav
    {
        private string _url;
        private string _username;
        private string _password;
        private string _authHeader;

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

        public async Task<bool> TestAlive(int timeOut, int retryTime)
        {
            try
            {
                string resault = await LoopAsync<string>(
                    () =>
                    {
                        return HttpWebResponseUtility.Operate(_url, "PROPFIND", _authHeader);
                    },
                    timeOut,
                    retryTime
                );
            }
            catch (Exception ex)
            {
                Log.Write("[WebDAV] Test WebDav Failed, message = " + ex.Message);
                return false;
            }

            return true;
        }

        private async Task<FuncResType> LoopAsync<FuncResType>(Func<FuncResType> func, int timeOut, int retryTime)
        {
            for (int i = 1; i <= retryTime; i++)
            {
                try
                {
                    return await RunAsync<FuncResType>(func);
                }
                catch (Exception ex)
                {
                    if (i == retryTime)
                    {
                        throw ex;
                    }
                }
                await Task.Delay(timeOut);
            }
            return default(FuncResType);
        }

        private async Task<FuncResType> RunAsync<FuncResType>(Func<FuncResType> t)
        {
            return await Task.Run(() => t());
        }
    }
}
