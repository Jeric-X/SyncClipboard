#nullable enable
namespace SyncClipboard.Service
{
    public class ServerService : Service
    {
        Microsoft.AspNetCore.Builder.WebApplication? app;
        protected override void StartService()
        {
            app = SyncClipboard.Server.Program.Start(5033, Env.Directory);
        }

        protected override void StopSerivce()
        {
            app?.StartAsync();
        }
    }
}
