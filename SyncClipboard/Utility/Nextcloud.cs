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
            public string token;
            public string endpoint;
        }
        public Poll poll;
        public string login;
    }

    class SecondResponse
    {
        public string server;
        public string loginName;
        public string appPassword;
    }

    #endregion

    static class Nextcloud
    {
        private const string VERIFICATION_URL = "/index.php/login/v2";
        private const string POLL_URL = "/login/v2/poll";
        public static void SignIn()
        {
            SignInFlowAsync();
        }

        public static async void SignInFlowAsync()
        {
            //string server = InputBox.Show("Please input Nextcloud server address", "https://");
            string server = "https://file.jericx.xyz:10443";
            if (server == string.Empty)
            {
                return;
            }

            string fistResponseJson;
            try
            {
                fistResponseJson = await GetFirstResponse(server);
            }
            catch (System.Net.WebException)
            {
                MessageBox.Show("Can not connect to the server:" + System.Environment.NewLine + server, "Error");
                return;
            }

            var firstResponse = DecodeJson<FistResponseJson>(fistResponseJson);
            System.Diagnostics.Process.Start(firstResponse.login);

            string secondResponseJson = await GetSecondResponse(server, firstResponse);
            var secondResponse = DecodeJson<SecondResponse>(fistResponseJson);

            if (secondResponse != null)
            {
                UserConfig.Config.SyncService.UserName = secondResponse.loginName;
                UserConfig.Config.SyncService.Password = secondResponse.appPassword;
                UserConfig.Config.SyncService.RemoteURL = secondResponse.server;
            }
        }

        private static async Task<string> GetFirstResponse(string server)
        {
            var loginUrl = server + VERIFICATION_URL;
            return await Task.Run(() =>
            {
                return HttpWebResponseUtility.Post(loginUrl);
            });
        }

        private static async Task<string> GetSecondResponse(string server, FistResponseJson firstResponse)
        {
            //var url = firstResponse.poll.endpoint;    // official document, but not usefull
            var url = server + POLL_URL;
            var token = $"token={firstResponse.poll.token}";
            return await Task.Run(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    try
                    {
                        return HttpWebResponseUtility.PostText(url, token);
                    }
                    catch (Exception ex)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }
                }
                return null;
            });
        }

        public static T DecodeJson<T>(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            T firstResponse = default(T);
            try
            {
                firstResponse = serializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error");
            }
            return firstResponse;
        }
    }
}
