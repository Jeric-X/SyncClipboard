using System;
using SyncClipboard.Module;
#nullable enable
namespace SyncClipboard.Service
{
    public class ServerService : Core.Interfaces.Service
    {
        Microsoft.AspNetCore.Builder.WebApplication? app;
        private event Action<bool>? SwitchChanged;
        public const string SERVICE_NAME = "内置服务器";
        public const string LOG_TAG = "INNERSERVER";
        protected override void StartService()
        {
            SwitchChanged += Global.Menu.AddMenuItemGroup(
                new string[] { SERVICE_NAME },
                new Action<bool>[] {
                    (switchOn) => {
                        UserConfig.Config.ServerService.SwitchOn = switchOn;
                        UserConfig.Save();
                    }
                }
            )[0];
            Load();
        }

        public override void Load()
        {
            SwitchChanged?.Invoke(UserConfig.Config.ServerService.SwitchOn);
            app?.StopAsync();
            if (UserConfig.Config.ServerService.SwitchOn)
            {
                app = Server.Program.Start(
                    UserConfig.Config.ServerService.Port,
                    Env.Directory,
                    UserConfig.Config.ServerService.UserName,
                    UserConfig.Config.ServerService.Password);
            }
        }

        protected override void StopSerivce()
        {
            app?.StopAsync();
        }
    }
}
