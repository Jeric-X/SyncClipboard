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
            public record class CProgram
            {
                public int IntervalTime { get; set; } = 3;
                public string Proxy { get; set; } = "";
                public bool DeleteTempFilesOnStartUp { get; set; } = false;
                public int LogRemainDays { get; set; } = 30;
            }

            public record class CCommandService
            {
                public bool SwitchOn { get; set; } = false;
                public int Shutdowntime { get; set; } = 30;
            }

            public record class CClipboardService
            {
                public bool ConvertSwitchOn { get; set; } = false;
            }

            public CCommandService CommandService { get; set; } = new CCommandService();
            public CClipboardService ClipboardService { get; set; } = new CClipboardService();
            public CProgram Program { get; set; } = new CProgram();
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
            try
            {
                var text = File.ReadAllText(_path);
                var config = JsonSerializer.Deserialize<Configuration>(text);
                ArgumentNullException.ThrowIfNull(config);
                Config = config;
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