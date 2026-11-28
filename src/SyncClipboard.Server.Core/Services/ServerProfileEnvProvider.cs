using System.Collections.Concurrent;
using System.Text.Json;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Server.Core.Services;

internal sealed record ClipboardAttachmentReferences(Dictionary<string, HashSet<string>?> Files, bool Complete);

public class ServerEnvProvider(IWebHostEnvironment env, ILogger<ServerEnvProvider>? logger = null) : IProfileEnv
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _clipboardLocks = new();

    public async Task<IDisposable> LockClipboard(string userId, CancellationToken token)
    {
        var gate = _clipboardLocks.GetOrAdd(userId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        return new ScopeGuard(() => gate.Release());
    }

    internal static bool InvalidFileName(string name)
    {
        return name is "." or ".." || Path.IsPathRooted(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('\\') || name.Contains('/');
    }

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
        UploadDirectory.CleanAbandoned(GetPersistentDir(), logger);
    }

    public ProfileDto? ReadClipboard(string userId) => ReadClipboard(userId, out _);

    private ProfileDto? ReadClipboard(string userId, out string? uploadedAs)
    {
        uploadedAs = null;
        var path = Path.Combine(GetClipboardDir(userId), "SyncClipboard.json");
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var document = JsonDocument.Parse(file);
            var root = document.RootElement;
            var profile = root.Deserialize<ProfileDto>();
            if (profile is not null && root.TryGetProperty("UploadedAs", out var alias) && alias.ValueKind == JsonValueKind.String)
                uploadedAs = alias.GetString();
            return profile;
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    internal ProfileDto? ReadUsableClipboard(string userId) => ReadUsableClipboard(userId, out _);

    internal ProfileDto? ReadUsableClipboard(string userId, out string? uploadedAs)
    {
        uploadedAs = null;
        try
        {
            return ReadClipboard(userId, out uploadedAs);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Invalid clipboard manifest for {UserId}", userId);
            return null;
        }
    }

    // Only the migrated single account can have clients that still request its original wire filenames.
    internal static string? DownloadName(string userId, ProfileType type, string? hash, string? storedName) =>
        userId != ServerSettingsStore.AdministratorId && type is (ProfileType.Group or ProfileType.Text) && hash is not null
            ? hash + (type == ProfileType.Group ? ".zip" : ".txt") : storedName;

    internal static ProfileDto PublicClipboard(string userId, ProfileDto dto) =>
        dto with { DataName = DownloadName(userId, dto.Type, dto.TransferDataHash, dto.DataName) };

    internal ClipboardAttachmentReferences ReadCurrentAttachments()
    {
        var users = Path.Combine(GetDataRootPath(), "users");
        var files = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
        var complete = true;
        if (Directory.Exists(users))
        {
            foreach (var dir in Directory.EnumerateDirectories(users))
            {
                try
                {
                    if (ReadClipboard(Path.GetFileName(dir)) is not { HasData: true } dto)
                        continue;
                    var key = Profile.GetWorkingDirName(dto.Type, dto.Hash);
                    if (string.IsNullOrEmpty(dto.DataName) || InvalidFileName(dto.DataName))
                    {
                        files[key] = null;
                        continue;
                    }
                    if (!files.TryGetValue(key, out var names))
                        files.Add(key, names = new(StringComparer.OrdinalIgnoreCase));
                    names?.Add(dto.DataName);
                }
                catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or UnauthorizedAccessException or InvalidDataException)
                {
                    complete = false;
                    logger?.LogWarning(ex, "Cannot read clipboard in {Directory}. Retaining potentially referenced files.", dir);
                }
            }
        }
        return new(files, complete);
    }

    public string GetDbDir()
    {
        return Path.Combine(GetDataRootPath(), "data");
    }
}
