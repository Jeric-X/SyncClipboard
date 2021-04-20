using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SyncClipboard.Control;

namespace SyncClipboard.Utility
{
    # region nextcloud official response defination
    class FistResponseJson
    {
        public class Poll
        {
            public string token = null;
            public string endpoint = null;
        }
        public Poll poll = null;
        public string login = null;
    }

    class SecondResponse
    {
        public string server = null;
        public string loginName = null;
        public string appPassword = null;
    }

    #endregion

    static class Nextcloud
    {
        private const string VERIFICATION_URL = "/index.php/login/v2";
        private const string WEBDAV_URL = "/remote.php/dav/files";
        private const int VERIFICATION_LIMITED_TIME = 60000;
        private const int INTERVAL_TIME = 1000;
        public static void SignIn()
        {
            SignInFlowAsync();
        }

        public static async void SignInFlowAsync()
        {
            string server = InputBox.Show("Please input Nextcloud server address", $"https://[请在确定后{VERIFICATION_LIMITED_TIME / 1000}秒内完成网页认证]");
            if (server == string.Empty)
            {
                return;
            }

            var fistResponseJson = await GetFirstResponse(server); ;
            var firstResponse = DecodeJson<FistResponseJson>(fistResponseJson);
            if (firstResponse == null)
            {
                return;
            }

            System.Diagnostics.Process.Start(firstResponse.login);

            string secondResponseJson = await GetSecondResponse(server, firstResponse);
            var secondResponse = DecodeJson<SecondResponse>(secondResponseJson);
            if (secondResponse == null)
            {
                return;
            }

            var path = InputBox.Show("Please input syncClipboard folder path");
            if (path == string.Empty)
            {
                return;
            }

            UserConfig.Config.SyncService.UserName = secondResponse.loginName;
            UserConfig.Config.SyncService.Password = secondResponse.appPassword;
            UserConfig.Config.SyncService.RemoteURL = $"{secondResponse.server}/{WEBDAV_URL}/{secondResponse.loginName}/{path}";
            UserConfig.Save();
        }

        private static async Task<string> GetFirstResponse(string server)
        {
            var loginUrl = server + VERIFICATION_URL;
            return await Task.Run(() =>
            {
                try
                {
                    return HttpWebResponseUtility.Post(loginUrl);
                }
                catch (System.Net.WebException)
                {
                    return "Can not connect to the server";
                }
                catch (UriFormatException)
                {
                    return "URL format is wrong";
                }
            });
        }

        private static async Task<string> GetSecondResponse(string server, FistResponseJson firstResponse)
        {
            var url = firstResponse.poll.endpoint;
            var token = $"token={firstResponse.poll.token}";
            return await Task.Run(() =>
            {
                for (int i = 0; i < VERIFICATION_LIMITED_TIME / INTERVAL_TIME; i++)
                {
                    try
                    {
                        return HttpWebResponseUtility.PostText(url, token);
                    }
                    catch
                    {
                        Thread.Sleep(INTERVAL_TIME);
                        continue;
                    }
                }
                return $"认证失败/{VERIFICATION_LIMITED_TIME / 1000}s超时";
            });
        }

        public static T DecodeJson<T>(string json)
        {
            T firstResponse = default(T);
            try
            {
                firstResponse = new JavaScriptSerializer().Deserialize<T>(json);
            }
            catch
            {
                MessageBox.Show("认证中发生错误：" + System.Environment.NewLine + json?.ToString(), "Error");
            }
            return firstResponse;
        }
    }
}
