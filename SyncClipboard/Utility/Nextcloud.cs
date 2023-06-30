using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        public class CPoll
        {
            public string Token { get; set; } = null;
            public string Endpoint { get; set; } = null;
        }
        public CPoll Poll { get; set; } = null;
        public string Login { get; set; } = null;
    }

    internal class SecondResponse
    {
        public string Server { get; set; } = null;
        public string LoginName { get; set; } = null;
        public string AppPassword { get; set; } = null;
    }

    #endregion

    public static class Nextcloud
    {
        private const string VERIFICATION_URL = "/index.php/login/v2";
        private const string WEBDAV_URL = "remote.php/dav/files";
        private const int VERIFICATION_LIMITED_TIME = 60000;
        private const int INTERVAL_TIME = 1000;

        public static async Task<NextcloudCredential> SignInFlowAsync()
        {
            string server = InputBox.Show("Please input Nextcloud server address", $"https://[请在确定后{VERIFICATION_LIMITED_TIME / 1000}秒内完成网页认证]");
            if (string.IsNullOrEmpty(server))
            {
                return null;
            }

            var firstResponse = await GetFirstResponse(server).ConfigureAwait(false);
            if (firstResponse == null)
            {
                return null;
            }

            Sys.OpenWithDefaultApp(firstResponse.Login);

            var secondResponse = await GetSecondResponse(firstResponse).ConfigureAwait(false);
            if (secondResponse == null)
            {
                MessageBox.Show("认证中发生错误：" + Environment.NewLine + "Pelease Login using broswer", "Error");
                return null;
            }

            var path = InputBox.Show("Please input syncClipboard folder path");
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return new NextcloudCredential
            {
                Username = secondResponse.LoginName,
                Password = secondResponse.AppPassword,
                Url = $"{secondResponse.Server}/{WEBDAV_URL}/{secondResponse.LoginName}/{path}"
            };
        }

        private static async Task<FistResponseJson> GetFirstResponse(string server)
        {
            var loginUrl = server + VERIFICATION_URL;
            try
            {
                return await Global.Http.PostTextRecieveJson<FistResponseJson>(loginUrl);
            }
            catch (System.Net.WebException)
            {
                MessageBox.Show("认证中发生错误：" + Environment.NewLine + "Can not connect to the server", "Error");
                return null;
            }
            catch (UriFormatException)
            {
                MessageBox.Show("认证中发生错误：" + Environment.NewLine + "URL format is wrong", "Error");
                return null;
            }
        }

        private static async Task<SecondResponse> GetSecondResponse(FistResponseJson firstResponse)
        {
            var url = firstResponse.Poll.Endpoint;
            for (int i = 0; i < VERIFICATION_LIMITED_TIME / INTERVAL_TIME; i++)
            {
                try
                {
                    KeyValuePair<string, string>[] list = { KeyValuePair.Create("token", firstResponse.Poll.Token) };
                    return await Global.Http.PostTextRecieveJson<SecondResponse>(url, list);
                }
                catch
                {
                    await Task.Delay(INTERVAL_TIME);
                }
            }
            return null;
        }
    }
}
