namespace SyncClipboard.Server.Core.Services;

public class ServerProfileEnvProvider(IWebHostEnvironment env) : IProfileEnv
{
    public string GetHistoryPersistentDir()
    {
        return Path.Combine(env.WebRootPath, "history");
    }

    public string GetPersistentDir()
    {
        return Path.Combine(env.WebRootPath, "history");
    }
}