namespace SyncClipboard.Shared.Profiles;

public interface IProfileEnv
{
    string GetPersistentDir();
    string GetHistoryPersistentDir();
}