using SyncClipboard.Core.Commons;
using System;
#nullable enable
namespace SyncClipboard.Service
{
    public class ServerService : Core.Interfaces.Service
    {
        Microsoft.AspNetCore.Builder.WebApplication? app;
        private event Action<bool>? SwitchChanged;
        public const string SERVICE_NAME = "内置服务器";
        public const string LOG_TAG = "INNERSERVER";

        private readonly UserConfig _userConfig;

        public ServerService(UserConfig userConfig)
        {
            _userConfig = userConfig;
        }

        protected override void StartService()
        {
            SwitchChanged += Global.Menu.AddMenuItemGroup(
                new string[] { SERVICE_NAME },
                new Action<bool>[] {
                    (switchOn) => {
                        _userConfig.Config.ServerService.SwitchOn = switchOn;
                        _userConfig.Save();
                    }
                }
            )[0];
            Load();
        }

        public override void Load()
        {
            SwitchChanged?.Invoke(_userConfig.Config.ServerService.SwitchOn);
            app?.StopAsync();
            if (_userConfig.Config.ServerService.SwitchOn)
            {
                app = Server.Program.Start(
                    _userConfig.Config.ServerService.Port,
                    Env.Directory,
                    _userConfig.Config.ServerService.UserName,
                    _userConfig.Config.ServerService.Password);
            }
        }

        protected override void StopSerivce()
        {
            app?.StopAsync();
        }
    }
}
