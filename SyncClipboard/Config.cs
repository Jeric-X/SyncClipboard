using System;
using System.Windows.Forms;

namespace SyncClipboard
{
    public static class Config
    {
        //public static String User { get; set; }
        //public static String Password { get; set; }

        private static String Auth { get; set; }

        public static string GetProfileUrl()
        {
            return UserConfig.Config.SyncService.RemoteURL + "/SyncClipboard.json";
        }

        public static string GetRemotePath()
        {
            return UserConfig.Config.SyncService.RemoteURL;
        }

        public static void Load()
        {
            Auth = FormatHttpAuthHeader(UserConfig.Config.SyncService.UserName, UserConfig.Config.SyncService.Password);
        }

        public static void Save()
        {
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
