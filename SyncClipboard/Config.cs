using System;
using System.Windows.Forms;

namespace SyncClipboard
{
    public static class Config
    {
        public static String RemoteURL { get; set; }
        public static String User { get; set; }
        public static String Password { get; set; }

        private static String Auth { get; set; }
        private static String Url { get; set; }

        public static string GetProfileUrl()
        {
            return Url + "/SyncClipboard.json";
        }

        public static string GetRemotePath()
        {
            return Url;
        }

        public static void Load()
        {
            try
            {
                RemoteURL = Properties.Settings.Default.URL;
                User = Properties.Settings.Default.USERNAME;
                Password = Properties.Settings.Default.PASSWORD;
            }
            catch
            { 
                if (MessageBox.Show("配置文件出错", "即将初始化默认配置", MessageBoxButtons.YesNo) == DialogResult.No)
                {
                    Application.Exit(); 
                }
            }
            
            Auth = FormatHttpAuthHeader(User, Password);
            Url = RemoteURL;
        }

        public static void Save()
        {
            Properties.Settings.Default.URL = RemoteURL;
            Properties.Settings.Default.USERNAME = User ;
            Properties.Settings.Default.PASSWORD = Password;
            Properties.Settings.Default.Save();
            Load();
            UserConfig.Save();
        }

        private static string FormatHttpAuthHeader(string user, string password)
        {
            string authHeader;
            byte[] bytes = System.Text.Encoding.Default.GetBytes(user + ":" + password);
            try
            {
                authHeader =  "Authorization: Basic " + Convert.ToBase64String(bytes);
            }
            catch
            {
                authHeader = user + ":" + password;
            }
            return authHeader;
        }

        public static string GetHttpAuthHeader()
        {
            return Config.Auth;
        } 
    }
}
