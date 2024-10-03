using SyncClipboard.Server.Controller;

namespace SyncClipboard.Server;

public class Program
{
    private const string ENV_VAR_USERNAME = "SYNCCLIPBOARD_USERNAME";
    private const string ENV_VAR_PASSWORD = "SYNCCLIPBOARD_PASSWORD";

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = args,
                WebRootPath = "server"
            }
        );

        var envUsername = Environment.GetEnvironmentVariable(ENV_VAR_USERNAME);
        var envPassword = Environment.GetEnvironmentVariable(ENV_VAR_PASSWORD);
        if (!string.IsNullOrEmpty(envUsername) && !string.IsNullOrEmpty(envPassword))
        {
            builder.Services.AddSingleton<ICredentialChecker, StaticCredentialChecker>(_ =>
                new StaticCredentialChecker(envUsername, envPassword)
            );
        }
        else
        {
            builder.Services.AddSingleton<ICredentialChecker, FileCredentialChecker>();
        }
        var app = Web.Configure(builder);
        app.UseSyncCliboardServer();
        app.Run();
    }
}