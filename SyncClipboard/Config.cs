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
        public static int TimeOut = 1*60*1000;

        public static String RemoteURL { get; set; }
        public static String User { get; set; }
        public static String Password { get; set; }
        public static String Auth { get; set; }

        public static void Load()
        {
            RemoteURL = Properties.Settings.Default.URL;
            User = Properties.Settings.Default.USERNAME;
            Password = Properties.Settings.Default.PASSWORD;
            Auth = GetAuth(User + ":" + Password);
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
