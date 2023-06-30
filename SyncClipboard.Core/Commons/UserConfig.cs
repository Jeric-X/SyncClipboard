using Microsoft.Extensions.Options;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Options;
using System.Runtime.Versioning;
using System.Text.Json;

namespace SyncClipboard.Core.Commons
{
    public class UserConfig
    {
        public event Action? ConfigChanged;

        public class Configuration
        {
            public class CProgram
            {
                public int IntervalTime { get; set; } = 3;
                public int RetryTimes { get; set; } = 3;
                public int TimeOut { get; set; } = 10;
                public string Proxy { get; set; } = "";
                public bool DeleteTempFilesOnStartUp { get; set; } = false;
                public int LogRemainDays { get; set; } = 30;
            }

            public class CSyncService
            {
                public string RemoteURL { get; set; } = "";
                public string UserName { get; set; } = "";
                public string Password { get; set; } = "";
                public bool PullSwitchOn { get; set; } = false;
                public bool PushSwitchOn { get; set; } = false;
                public bool DeletePreviousFilesOnPush { get; set; } = true;
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

            public class CServerService
            {
                public bool SwitchOn { get; set; } = false;
                public short Port { get; set; } = 5033;
                public string UserName { get; set; } = "admin";
                public string Password { get; set; } = "admin";
            }

            public CSyncService SyncService { get; set; } = new CSyncService();
            public CCommandService CommandService { get; set; } = new CCommandService();
            public CClipboardService ClipboardService { get; set; } = new CClipboardService();
            public CProgram Program { get; set; } = new CProgram();
            public CServerService ServerService { get; set; } = new CServerService();
        }

        public Configuration Config = new();

        private readonly ILogger _logger;
        private readonly IContextMenu _contextMenu;
        private readonly string _path;

        public UserConfig(IOptions<UserConfigOption> option, ILogger logger, IContextMenu contextMenu)
        {
            _logger = logger;
            _contextMenu = contextMenu;
            _path = option.Value.Path ?? throw new ArgumentNullException(nameof(option.Value.Path), "配置文件路径为null");
            Load();
        }

        public void Save()
        {
            var configStr = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            _logger.Write("Save user config");
            File.WriteAllText(_path, configStr);
            ConfigChanged?.Invoke();
        }

        public void Load()
        {
            string text;
            try
            {
                text = File.ReadAllText(_path);
            }
            catch (FileNotFoundException)
            {
                WriteDefaultConfigFile();
                return;
            }

            try
            {
                var config = JsonSerializer.Deserialize<Configuration>(text);
                if (config is null)
                {
                    WriteDefaultConfigFile();
                }
                else
                {
                    Config = config;
                }
                ConfigChanged?.Invoke();
            }
            catch
            {
                WriteDefaultConfigFile();
            }
        }

        private void WriteDefaultConfigFile()
        {
            _logger.Write("Write new default file.");
            Config = new Configuration();
            Save();
        }

        [SupportedOSPlatform("windows")]
        public void AddMenuItems()
        {
            MenuItem[] menuItems =
            {
                new MenuItem(
                    "打开配置文件", () => {
                        var open = new System.Diagnostics.Process();
                        open.StartInfo.FileName = "notepad";
                        open.StartInfo.Arguments = _path;
                        open.Start();
                    } )  ,
                new MenuItem(
                    "打开配置文件所在位置", () => {
                        var open = new System.Diagnostics.Process();
                        open.StartInfo.FileName = "explorer";
                        open.StartInfo.Arguments = "/e,/select," + _path;
                        open.Start();
                    }),
                new MenuItem("重新载入配置文件", () => this.Load())
            };
            _contextMenu.AddMenuItemGroup(menuItems);
        }
    }
}