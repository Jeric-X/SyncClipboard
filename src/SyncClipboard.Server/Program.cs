using SyncClipboard.Server.Core;
using SyncClipboard.Server.Core.CredentialChecker;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Server;

public class Program
{
    private const string ENV_VAR_USERNAME = "SYNCCLIPBOARD_USERNAME";
    private const string ENV_VAR_PASSWORD = "SYNCCLIPBOARD_PASSWORD";
    private const string ENV_VAR_RELOAD_ON_CHANGE = "ASPNETCORE_hostBuilder__reloadConfigOnChange";

    public static void Main(string[] args)
    {
        Console.WriteLine("Starting SyncClipboard Server...");
        var reloadOnChangeEnvStr = Environment.GetEnvironmentVariable(ENV_VAR_RELOAD_ON_CHANGE);
        if (string.IsNullOrEmpty(reloadOnChangeEnvStr))
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_hostBuilder__reloadConfigOnChange", "false");
        }

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = args
            }
        );

        EnsureAppSettingsExists(builder.Environment.ContentRootPath, builder.Configuration);

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
        builder.Services.Configure<AppSettings>(builder.Configuration.GetSection(nameof(AppSettings)));
        var app = Web.Configure(builder);
        app.Run();
    }

    private static void EnsureAppSettingsExists(string contentRootPath, ConfigurationManager configurationManager)
    {
        var targetAppSettingsPath = Path.Combine(contentRootPath, "appsettings.json");

        if (!File.Exists(targetAppSettingsPath))
        {
            var programDirectory = AppContext.BaseDirectory;
            var defaultAppSettingsPath = Path.Combine(programDirectory, "appsettings.json");

            if (!File.Exists(defaultAppSettingsPath))
            {
                Console.WriteLine($"Error: default appsettings.json not found at {defaultAppSettingsPath}");
                throw new FileNotFoundException("Error: default appsettings.json not found", defaultAppSettingsPath);
            }

            try
            {
                File.Copy(defaultAppSettingsPath, targetAppSettingsPath);
                configurationManager.AddJsonFile(targetAppSettingsPath);
                Console.WriteLine($"Copied default appsettings.json to {targetAppSettingsPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to copy appsettings.json: {ex.Message}");
                throw;
            }
        }
    }
}