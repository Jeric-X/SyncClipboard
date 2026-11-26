using SyncClipboard.Server.Core;
using SyncClipboard.Server.Core.Services;
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

        var settings = LoadSettings(builder.Environment.ContentRootPath, builder.Configuration);
        var app = Web.Configure(builder, settings);
        app.Run();
    }

    private static ServerSettingsStore LoadSettings(string root, ConfigurationManager configuration)
    {
        var name = Environment.GetEnvironmentVariable(ENV_VAR_USERNAME);
        var password = Environment.GetEnvironmentVariable(ENV_VAR_PASSWORD);
        AccountUpdate? bootstrap = !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(password) ? new(name, password) : null;
        if (File.Exists(Path.Combine(root, ServerSettingsStore.FileName)))
            return new ServerSettingsStore(root, new(), bootstrap);

        var legacy = configuration.GetSection("AppSettings");
        if (bootstrap is null && !Directory.Exists(Path.Combine(root, "server", "users"))
            && (legacy.Exists() || File.Exists(Path.Combine(root, "server", "data", "history.db"))))
            bootstrap = new(legacy["UserName"] ?? "admin", legacy["Password"] ?? "admin");

        return new ServerSettingsStore(root, legacy.Get<AppSettings>() ?? new(), bootstrap);
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
                // Reload the new file without changing environment or command-line configuration priority.
                ((IConfigurationRoot)configurationManager).Reload();
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
