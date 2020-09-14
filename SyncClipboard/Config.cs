using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncClipboard
{
    public static class Config
    {
        public static int IntervalTime = 3000;
        public static int RetryTimes = 3;
        public static int TimeOut = 10000;

        public static bool IsCustomServer { get; set; }
        public static String CustomName { get; set; }
        public static bool IfPull { get; set; }
        public static bool IfPush { get; set; }
        public static String RemoteURL { get; set; }
        public static String User { get; set; }
        public static String Password { get; set; }

        public static String Auth { get; set; }
        private static String Url { get; set; }

        public static string GetProfileUrl()
        {
            return Url + "/SyncClipboard.json";
        }

        public static string GetImageUrl()
        {
            return Url + "/image.bmg";
        }

        public static void Load()
        {
            RemoteURL = Properties.Settings.Default.URL;
            User = Properties.Settings.Default.USERNAME;
            Password = Properties.Settings.Default.PASSWORD;
            IfPull = Properties.Settings.Default.IFPULL;
            IfPush = Properties.Settings.Default.IFPUSH;
            IsCustomServer = Properties.Settings.Default.ISCUSTOMSERVER;
            CustomName = Properties.Settings.Default.CUSTOMNAME;
            IntervalTime = Properties.Settings.Default.IntervalTime;
            RetryTimes = Properties.Settings.Default.RetryTimes;
            TimeOut = Properties.Settings.Default.TimeOut;

            if (IsCustomServer)
            {
                Auth = GetAuth(User + ":" + Password);
                Url = RemoteURL;
            }
            else
            {
                Auth = GetAuth(Program.DefaultUser + ":" + Program.DefaultPassword);
                Url = Program.DefaultServer + CustomName;
            }
        }

        public static void Save()
        {
            Properties.Settings.Default.URL = RemoteURL;
            Properties.Settings.Default.USERNAME = User ;
            Properties.Settings.Default.PASSWORD = Password;
            Properties.Settings.Default.IFPULL = IfPull;
            Properties.Settings.Default.IFPUSH = IfPush;
            Properties.Settings.Default.CUSTOMNAME = CustomName;
            Properties.Settings.Default.ISCUSTOMSERVER = IsCustomServer;
            Properties.Settings.Default.IntervalTime = IntervalTime;
            Properties.Settings.Default.RetryTimes = RetryTimes;
            Properties.Settings.Default.TimeOut = TimeOut;
            Properties.Settings.Default.Save();
        }

        private static String GetAuth(String source)
        {
            String Auth;
            byte[] bytes = System.Text.Encoding.Default.GetBytes(source);
            try
            {
                Auth = Convert.ToBase64String(bytes);
            }
            catch
            {
                Auth = source;
            }
            return Auth;
        }
    }
}
