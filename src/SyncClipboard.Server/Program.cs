using SyncClipboard.Server.Controller;

namespace SyncClipboard.Server;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = args,
                WebRootPath = "server"
            }
        );
        builder.Services.AddSingleton<ICredentialChecker, FileCredentialChecker>();
        var app = Web.Configure(builder);
        app.UseSyncCliboardServer();
        app.Run();
    }
}