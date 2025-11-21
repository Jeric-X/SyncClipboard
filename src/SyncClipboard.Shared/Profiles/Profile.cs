using System.Diagnostics.CodeAnalysis;
using SyncClipboard.Shared.Profiles.Models;

namespace SyncClipboard.Shared.Profiles;

public abstract class Profile
{
    public abstract ProfileType Type { get; }
    public abstract string DisplayText { get; }
    public abstract string ShortDisplayText { get; }
    public abstract Task<bool> IsLocalDataValid(bool quick, CancellationToken token);
    public abstract Task<ClipboardProfileDTO> ToDto(CancellationToken token);
    public abstract ValueTask<long> GetSize(CancellationToken token);
    public abstract ValueTask<string> GetHash(CancellationToken token);
    public abstract Task<ProfilePersistentInfo> Persistentize(CancellationToken token);
    public abstract Task<ProfileLocalInfo> Localize(CancellationToken token);

    public abstract bool HasTransferData { get; }
    public abstract Task<string?> PrepareTransferData(CancellationToken token);
    public abstract Task SetTranseferData(string path, bool verify, CancellationToken token);
    public abstract bool NeedsTransferData([NotNullWhen(true)] out string? dataPath);

    public async Task<string> GetProfileId(CancellationToken token)
    {
        return GetProfileId(Type, await GetHash(token));
    }

    public static string GetProfileId(ProfileType type, string hash)
    {
        return $"{type}-{hash}";
    }

    public static bool ParseProfileId(string profileId, out ProfileType type, [NotNullWhen(true)] out string? hash)
    {
        var parts = profileId.Split('-', 2);
        if (parts.Length != 2)
        {
            type = ProfileType.None;
            hash = null;
            return false;
        }

        if (!Enum.TryParse(parts[0], out type))
        {
            type = ProfileType.None;
            hash = null;
            return false;
        }

        hash = parts[1];
        return true;
    }

    public static async Task<bool> Same(Profile? lhs, Profile? rhs, CancellationToken token)
    {
        if (ReferenceEquals(lhs, rhs))
        {
            return true;
        }
        if (lhs is null)
        {
            return rhs is null;
        }
        if (rhs is null)
        {
            return false;
        }
        if (lhs.GetType() != rhs.GetType())
        {
            return false;
        }

        var lHash = await lhs.GetHash(token);
        var rHash = await rhs.GetHash(token);
        return string.Equals(lHash, rHash, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        throw new NotSupportedException("Use Profile.Same to compare two profiles.");
    }

    public override int GetHashCode()
    {
        throw new NotImplementedException();
    }

    public static IProfileEnv? ProfileEnv { get; private set; }

    public static void SetGlobalProfileEnvProvider(IProfileEnv provider)
    {
        ProfileEnv = provider;
    }

    protected async Task<string> GetWorkingDirectory(CancellationToken token)
    {
        return GetWorkingDirectory(await GetHash(token));
    }

    protected string GetWorkingDirectory(string hash)
    {
        return GetWorkingDirectory(Type, hash);
    }

    public static string GetWorkingDirectory(ProfileType type, string hash)
    {
        var provider = ProfileEnv ?? throw new InvalidOperationException("Profile working directory provider is not set.");
        var dirName = $"{type}_{hash}";
        var allProfileDir = provider.GetWorkingDir();
        var profileDir = Path.Combine(allProfileDir, dirName);
        if (!Directory.Exists(profileDir))
            Directory.CreateDirectory(profileDir);
        return profileDir;
    }

    [return: NotNullIfNotNull(nameof(fullPath))]
    protected static string? GetPersistentPath(string workingDir, string? fullPath)
    {
        if (fullPath is null)
        {
            return null;
        }

        var relativePath = Path.GetRelativePath(workingDir, fullPath);

        // 如果相对路径以..开头，说明fullPath不在workingDir的子目录中，返回完整路径
        if (relativePath.StartsWith(".."))
        {
            return fullPath;
        }

        return relativePath;
    }

    [return: NotNullIfNotNull(nameof(persistentPath))]
    public static string? GetFullPath(string workingDir, string? persistentPath)
    {
        if (persistentPath is null)
        {
            return null;
        }

        if (Path.IsPathRooted(persistentPath))
        {
            return persistentPath;
        }
        return Path.Combine(workingDir, persistentPath);
    }

    public static Profile Create(ProfilePersistentInfo persistentEntity)
    {
        var workingDir = GetWorkingDirectory(persistentEntity.Type, persistentEntity.Hash);
        var entity = persistentEntity with
        {
            TransferDataFile = GetFullPath(workingDir, persistentEntity.TransferDataFile),
            FilePaths = persistentEntity.FilePaths.Select(path => GetFullPath(workingDir, path)).ToArray(),
        };

        return persistentEntity.Type switch
        {
            ProfileType.Text => new TextProfile(entity),
            ProfileType.File => new FileProfile(entity),
            ProfileType.Image => new ImageProfile(entity),
            ProfileType.Group => new GroupProfile(entity),
            _ => throw new NotSupportedException($"Unsupported profile type from Persistent: {entity.Type}"),
        };
    }
}
