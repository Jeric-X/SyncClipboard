namespace SyncClipboard.Server.Core.Services;

public class ServerProfileEnvProvider(IWebHostEnvironment env) : IProfileEnv
{
    public Task<string> GetWorkingDir(CancellationToken token)
    {
        return Task.FromResult(Path.Combine(env.WebRootPath, "history"));
    }
}