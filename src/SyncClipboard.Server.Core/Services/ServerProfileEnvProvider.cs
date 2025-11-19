namespace SyncClipboard.Server.Core.Services;

public class ServerProfileEnvProvider(IWebHostEnvironment env) : IProfileEnv
{
    public string GetWorkingDir()
    {
        return Path.Combine(env.WebRootPath, "history");
    }
}