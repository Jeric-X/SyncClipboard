namespace SyncClipboard.Shared.Profiles;

public interface IProfileEnv
{
    Task<string> GetWorkingDir(CancellationToken token);
}