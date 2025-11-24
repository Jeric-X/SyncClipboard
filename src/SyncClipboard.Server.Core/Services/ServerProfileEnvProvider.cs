namespace SyncClipboard.Server.Core.Services;

public class ServerEnvProvider(IWebHostEnvironment env) : IProfileEnv
{
    public string GetHistoryPersistentDir()
    {
        return Path.Combine(GetDataRootPath(), "history");
    }

    public string GetPersistentDir()
    {
        return Path.Combine(GetDataRootPath(), "history");
    }

    public string GetDataRootPath()
    {
        return Path.Combine(env.ContentRootPath, "server");
    }

    public string GetDbDir()
    {
        return Path.Combine(GetDataRootPath(), "data");
    }
}