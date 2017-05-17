using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncClipboard
{
    public static class Config
    {
        public static uint IntervalTime = 3000;
        public static int RetryTimes = 3;

        public static String RemoteURL { get; set; }
        public static String User { get; set; }
        public static String Password { get; set; }

        public static void Load()
        {
            RemoteURL = Properties.Settings.Default.URL;
            User = Properties.Settings.Default.USERNAME;
            Password = Properties.Settings.Default.PASSWORD;
        }
        
    }
}
