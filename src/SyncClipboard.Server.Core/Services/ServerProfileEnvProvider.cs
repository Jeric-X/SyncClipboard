using System.Text.Json;

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

    public string GetClipboardDir(string userId) => Path.Combine(GetDataRootPath(), "users", userId);

    public void MigrateLegacyClipboard()
    {
        var root = GetDataRootPath();
        var target = GetClipboardDir(ServerSettingsStore.AdministratorId);
        Directory.CreateDirectory(target);
        if (File.Exists(Path.Combine(root, "SyncClipboard.json")))
            File.Move(Path.Combine(root, "SyncClipboard.json"), Path.Combine(target, "SyncClipboard.json"));
        if (Directory.Exists(Path.Combine(root, "file")))
            Directory.Move(Path.Combine(root, "file"), Path.Combine(target, "file"));
    }

    public ProfileDto? ReadClipboard(string userId)
    {
        var path = Path.Combine(GetClipboardDir(userId), "SyncClipboard.json");
        if (!File.Exists(path))
            return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return JsonSerializer.Deserialize<ProfileDto>(file);
    }

    public HashSet<string> CurrentAttachmentDirectories()
    {
        var users = Path.Combine(GetDataRootPath(), "users");
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(users))
        {
            foreach (var dir in Directory.EnumerateDirectories(users))
            {
                if (ReadClipboard(Path.GetFileName(dir)) is { HasData: true } dto)
                    directories.Add(Profile.GetWorkingDirName(dto.Type, dto.Hash));
            }
        }
        return directories;
    }

    public string GetDbDir()
    {
        return Path.Combine(GetDataRootPath(), "data");
    }
}
