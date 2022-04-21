using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows.Forms;
using SyncClipboard.Control;

namespace SyncClipboard.Utility
{
    public class NextcloudCredential
    {
        public string Username;
        public string Password;
        public string Url;
    }

    # region nextcloud official response defination
    internal class FistResponseJson
    {
        public class Poll
        {
            public string token = null;
            public string endpoint = null;
        }
        public Poll poll = null;
        public string login = null;
    }

    internal class SecondResponse
    {
        public string server = null;
        public string loginName = null;
        public string appPassword = null;
    }

    #endregion

    public static class Nextcloud
    {
        private const string VERIFICATION_URL = "/index.php/login/v2";
        private const string WEBDAV_URL = "remote.php/dav/files";
        private const int VERIFICATION_LIMITED_TIME = 60000;
        private const int INTERVAL_TIME = 1000;
        private const int TIMEOUT = 10000;

        public static async Task<NextcloudCredential> SignInFlowAsync()
        {
            string server = InputBox.Show("Please input Nextcloud server address", $"https://[请在确定后{VERIFICATION_LIMITED_TIME / 1000}秒内完成网页认证]");
            if (string.IsNullOrEmpty(server))
            {
                return null;
            }

            var fistResponseJson = await GetFirstResponse(server).ConfigureAwait(false);
            var firstResponse = DecodeJson<FistResponseJson>(fistResponseJson);
            if (firstResponse == null)
            {
                return null;
            }

            System.Diagnostics.Process.Start(firstResponse.login);

            string secondResponseJson = await GetSecondResponse(firstResponse).ConfigureAwait(false);
            var secondResponse = DecodeJson<SecondResponse>(secondResponseJson);
            if (secondResponse == null)
            {
                return null;
            }

            var path = InputBox.Show("Please input syncClipboard folder path");
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return new NextcloudCredential {
                Username = secondResponse.loginName,
                Password = secondResponse.appPassword,
                Url = $"{secondResponse.server}/{WEBDAV_URL}/{secondResponse.loginName}/{path}"
            };
    }

        private static async Task<string> GetFirstResponse(string server)
        {
            var loginUrl = server + VERIFICATION_URL;
            return await Task.Run(() =>
            {
                try
                {
                    return HttpWeb.Post(loginUrl, new HttpPara { Timeout = TIMEOUT });
                }
                catch (System.Net.WebException)
                {
                    return "Can not connect to the server";
                }
                catch (UriFormatException)
                {
                    return "URL format is wrong";
                }
            }).ConfigureAwait(false);
        }

        private static async Task<string> GetSecondResponse(FistResponseJson firstResponse)
        {
            var url = firstResponse.poll.endpoint;
            var token = $"token={firstResponse.poll.token}";
            return await Task.Run(() =>
            {
                for (int i = 0; i < VERIFICATION_LIMITED_TIME / INTERVAL_TIME; i++)
                {
                    try
                    {
                        return HttpWeb.Post(url, new HttpPara { Timeout = TIMEOUT }, token);
                    }
                    catch
                    {
                        Thread.Sleep(INTERVAL_TIME);
                    }
                }
                return $"认证失败/{VERIFICATION_LIMITED_TIME / 1000}s超时";
            }).ConfigureAwait(false);
        }

        public static T DecodeJson<T>(string json)
        {
            T firstResponse = default;
            try
            {
                firstResponse = JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                MessageBox.Show("认证中发生错误：" + System.Environment.NewLine + json?.ToString(), "Error");
            }
            return firstResponse;
        }
    }
}
