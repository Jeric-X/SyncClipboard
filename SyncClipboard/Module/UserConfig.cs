using System.Web.Script.Serialization;
using SyncClipboard.Utility;
using System.IO;
using System.Windows.Forms;
using System;

namespace SyncClipboard.Module
{
    internal static class UserConfig
    {
        internal static event Action ConfigChanged;
        private const string CONFIG_FILE = "SyncClipboard.json";
        internal class Configuration
        {
            public class CProgram
            {
                public int IntervalTime = 3000;
                public int RetryTimes = 3;
                public int TimeOut = 10000;
            }

            public class CSyncService
            {
                public string RemoteURL = "";
                public string UserName = "";
                public string Password = "";
                public bool PullSwitchOn = false;
                public bool PushSwitchOn = false;
                public bool IsNextcloud = false;
            }

            public class CCommandService
            {
                public bool switchOn = false;
                public int Shutdowntime = 30;
            }

            public CSyncService SyncService = new CSyncService();
            public CCommandService CommandService = new CCommandService();
            public CProgram Program = new CProgram();
        }

        internal static Configuration Config = new Configuration();

        internal static void Save()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            var configStr = serializer.Serialize(Config);
            try
            {
                File.WriteAllText(Env.FullPath(CONFIG_FILE), configStr);
            }
            catch
            {
                MessageBox.Show("Config file failed to save.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            ConfigChanged?.Invoke();
            Auth = FormatHttpAuthHeader(UserConfig.Config.SyncService.UserName, UserConfig.Config.SyncService.Password);
        }

        internal static void Load()
        {
            string text;
            try
            {
                text = File.ReadAllText(Env.FullPath(CONFIG_FILE));
            }
            catch (FileNotFoundException)
            {
                WriteDefaultConfigFile();
                return;
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            try
            {
                Config = serializer.Deserialize<Configuration>(text);
                if (Config is null)
                {
                    WriteDefaultConfigFile();
                }
            }
            catch
            {
                WriteDefaultConfigFile();
            }

            Auth = FormatHttpAuthHeader(Config.SyncService.UserName, Config.SyncService.Password);
        }

        private static void WriteDefaultConfigFile()
        {
            Log.Write("Write new default file.");
            Config = new Configuration();
            Save();
        }

        #region  TO BE MODIFIED
        // TO BE MODIFIED
        private static String Auth { get; set; }

        public static string GetProfileUrl()
        {
            return UserConfig.Config.SyncService.RemoteURL + "/SyncClipboard.json";
        }

        public static string GetRemotePath()
        {
            return UserConfig.Config.SyncService.RemoteURL;
        }

        private static string FormatHttpAuthHeader(string user, string password)
        {
            string authHeader;
            byte[] bytes = System.Text.Encoding.Default.GetBytes(user + ":" + password);
            try
            {
                authHeader = "Authorization: Basic " + Convert.ToBase64String(bytes);
            }
            catch
            {
                authHeader = user + ":" + password;
            }
            return authHeader;
        }

        public static string GetHttpAuthHeader()
        {
            return UserConfig.Auth;
        }

        #endregion
    }
}