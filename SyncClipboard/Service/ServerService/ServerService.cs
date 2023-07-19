using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
#nullable enable
namespace SyncClipboard.Service
{
    public class ServerService : Core.Interfaces.Service
    {
        Microsoft.AspNetCore.Builder.WebApplication? app;
        public const string SERVICE_NAME = "内置服务器";
        public const string LOG_TAG = "INNERSERVER";

        private readonly UserConfig _userConfig;
        private readonly ToggleMenuItem _toggleMenuItem;

        private readonly IContextMenu _contextMenu;

        public ServerService(UserConfig userConfig, IContextMenu contextMenu)
        {
            _userConfig = userConfig;
            _contextMenu = contextMenu;
            _toggleMenuItem = new ToggleMenuItem(
                SERVICE_NAME,
                _userConfig.Config.ServerService.SwitchOn,
                (status) =>
                {
                    _userConfig.Config.ServerService.SwitchOn = status;
                    _userConfig.Save();
                }
            );
        }

        protected override void StartService()
        {
            _contextMenu.AddMenuItem(_toggleMenuItem);
            Load();
        }

        public override void Load()
        {
            _toggleMenuItem.Checked = _userConfig.Config.ServerService.SwitchOn;
            app?.StopAsync();
            if (_userConfig.Config.ServerService.SwitchOn)
            {
                app = Server.Program.Start(
                    _userConfig.Config.ServerService.Port,
                    Env.Directory,
                    _userConfig.Config.ServerService.UserName,
                    _userConfig.Config.ServerService.Password
                );
            }
        }

        protected override void StopSerivce()
        {
            app?.StopAsync();
        }
    }
}
