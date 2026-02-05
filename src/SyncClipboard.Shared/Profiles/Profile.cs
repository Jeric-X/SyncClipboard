using System.Diagnostics.CodeAnalysis;
using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Profiles;

public abstract class Profile
{
    protected string? Hash;
    protected long? Size;
    protected readonly SemaphoreSlim _hashInitLock = new(1, 1);

    public abstract ProfileType Type { get; }
    public abstract string DisplayText { get; }
    public abstract string ShortDisplayText { get; }
    public abstract Task<bool> IsLocalDataValid(bool quick, CancellationToken token);
    public abstract Task<ProfileDto> ToProfileDto(CancellationToken token);
    protected abstract Task ComputeHash(CancellationToken token);
    protected abstract Task ComputeSize(CancellationToken token);

    public async ValueTask<long> GetSize(CancellationToken token)
    {
        if (Size is not null)
        {
            return Size.Value;
        }

        await _hashInitLock.WaitAsync(token);
        try
        {
            if (Size is not null)
            {
                return Size.Value;
            }

            await ComputeSize(token);
            return Size ?? 0;
        }
        finally
        {
            _hashInitLock.Release();
        }
    }

    public async ValueTask<string> GetHash(CancellationToken token)
    {
        if (Hash is not null)
        {
            return Hash;
        }

        await _hashInitLock.WaitAsync(token);
        try
        {
            if (Hash is not null)
            {
                return Hash;
            }

            await ComputeHash(token);
            return Hash ?? string.Empty;
        }
        finally
        {
            _hashInitLock.Release();
        }
    }
    public abstract Task<ProfilePersistentInfo> Persist(string persistentDir, CancellationToken token);
    public abstract Task<ProfileLocalInfo> Localize(string localDir, bool quick, CancellationToken token);
    public abstract void CopyTo(Profile target);

    public abstract bool HasTransferData { get; }
    public abstract Task<string?> PrepareTransferData(string persistentDir, CancellationToken token);
    public abstract Task SetTransferData(string path, bool verify, CancellationToken token);
    public abstract Task SetAndMoveTransferData(string persistentDir, string path, CancellationToken token);
    public abstract Task<string?> NeedsTransferData(string persistentDir, CancellationToken token);

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

    public string CreateWorkingDir(string persistentDir, string hash)
    {
        return CreateWorkingDir(persistentDir, Type, hash);
    }

    public static string CreateWorkingDir(string persistentDir, ProfileType type, string hash)
    {
        var profileDir = QueryGetWorkingDir(persistentDir, type, hash);
        if (!Directory.Exists(profileDir))
            Directory.CreateDirectory(profileDir);
        return profileDir;
    }

    public static string QueryGetWorkingDir(string persistentDir, ProfileType type, string hash)
    {
        if (hash.Contains(Path.DirectorySeparatorChar) || hash.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Hash contains invalid path characters.", nameof(hash));
        }

        var dirName = $"{type}_{hash}";
        var profileDir = Path.Combine(persistentDir, dirName);
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

    [return: NotNullIfNotNull(nameof(persistentPath))]
    public static string? GetFullPath(string persistentDir, ProfileType type, string hash, string? persistentPath)
    {
        var workingDir = QueryGetWorkingDir(persistentDir, type, hash);
        return GetFullPath(workingDir, persistentPath);
    }

    public static Profile Create(string persistentDir, ProfilePersistentInfo persistentEntity)
    {
        var workingDir = QueryGetWorkingDir(persistentDir, persistentEntity.Type, persistentEntity.Hash);
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

    public static Profile Create(ProfileDto dto)
    {
        return dto.Type switch
        {
            ProfileType.Text => new TextProfile(dto),
            ProfileType.File => dto.DataName is not null && ImageTool.FileIsImage(dto.DataName)
                ? new ImageProfile(dto)
                : new FileProfile(dto),
            ProfileType.Image => new ImageProfile(dto),
            ProfileType.Group => new GroupProfile(dto),
            _ => throw new NotSupportedException($"Unsupported profile type from ProfileDto: {dto.Type}"),
        };
    }
}
