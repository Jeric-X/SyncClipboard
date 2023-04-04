using System.Text.Json;
using SyncClipboard.Utility;
using System.IO;
using System.Windows.Forms;
using System;

namespace SyncClipboard.Module
{
    internal static class UserConfig
    {
        internal static event Action ConfigChanged;
        public const string CONFIG_FILE = "SyncClipboard.json";
        internal class Configuration
        {
            public class CProgram
            {
                public int IntervalTime { get; set; } = 3;
                public int RetryTimes { get; set; } = 3;
                public int TimeOut { get; set; } = 10;
                public string Proxy { get; set; } = "";
            }

            public class CSyncService
            {
                public string RemoteURL { get; set; } = "";
                public string UserName { get; set; } = "";
                public string Password { get; set; } = "";
                public bool PullSwitchOn { get; set; } = false;
                public bool PushSwitchOn { get; set; } = false;
                public bool EasyCopyImageSwitchOn { get; set; } = false;
                public int MaxFileByte { get; set; } = 1024 * 1024 * 20;  // 10MB
            }

            public class CCommandService
            {
                public bool SwitchOn { get; set; } = false;
                public int Shutdowntime { get; set; } = 30;
            }

            public class CClipboardService
            {
                public bool ConvertSwitchOn { get; set; } = false;
            }

            public CSyncService SyncService { get; set; } = new CSyncService();
            public CCommandService CommandService { get; set; } = new CCommandService();
            public CClipboardService ClipboardService { get; set; } = new CClipboardService();
            public CProgram Program { get; set; } = new CProgram();
        }

        internal static Configuration Config = new();

        internal static void Save()
        {
            var configStr = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            try
            {
                File.WriteAllText(Env.FullPath(CONFIG_FILE), configStr);
            }
            catch
            {
                MessageBox.Show("Config file failed to save.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            ConfigChanged?.Invoke();
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

            try
            {
                Config = JsonSerializer.Deserialize<Configuration>(text);
                if (Config is null)
                {
                    WriteDefaultConfigFile();
                }
                ConfigChanged?.Invoke();
            }
            catch
            {
                WriteDefaultConfigFile();
            }
        }

        private static void WriteDefaultConfigFile()
        {
            Log.Write("Write new default file.");
            Config = new Configuration();
            Save();
        }
    }
}