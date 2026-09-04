using System.Diagnostics.CodeAnalysis;
using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Profiles;

public abstract class Profile
{
    protected string? Hash;
    protected long? Size;
    protected readonly SemaphoreSlim _hashInitLock = new(1, 1);
    private string? _verifiedTransferDataPath;
    public string? TransferDataHash { get; protected set; }
    public bool HasVerifiedTransferDataHashBinding { get; private protected set; }

    public abstract ProfileType Type { get; }
    public abstract string DisplayText { get; }
    public abstract string ShortDisplayText { get; }
    public abstract Task<bool> IsLocalDataValid(bool quick, CancellationToken token);
    public abstract Task<ProfileDto> ToProfileDto(CancellationToken token);
    protected abstract Task ComputeHash(CancellationToken token);
    protected abstract Task ComputeSize(CancellationToken token);
    public bool HasKnownSize => Size is not null;

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
    public abstract Task SetTransferData(
        string path,
        bool verify,
        CancellationToken token);

    public abstract Task SetTransferData(
        string path,
        string transferDataHash,
        bool verify,
        CancellationToken token);

    public abstract Task SetAndMoveTransferData(
        string persistentDir,
        string path,
        CancellationToken token);
    public abstract Task<string?> NeedsTransferData(string persistentDir, CancellationToken token);

    protected static string NormalizeVerifiedTransferDataHash(string transferDataHash)
    {
        return NormalizeTransferDataHash(transferDataHash)
            ?? throw new ArgumentException("Transfer data hash cannot be empty.", nameof(transferDataHash));
    }

    public static bool IsValidTransferDataHash(string? hash)
    {
        return hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    }

    public static string? NormalizeTransferDataHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        if (!IsValidTransferDataHash(hash))
        {
            throw new ArgumentException("Transfer data hash must be a 64-character SHA-256 hex string.", nameof(hash));
        }

        return hash.ToUpperInvariant();
    }

    protected static string? RestoreTransferDataHash(string? hash)
    {
        return IsValidTransferDataHash(hash) ? hash!.ToUpperInvariant() : null;
    }

    protected void RestoreTransferDataHashBinding(string? hash, bool bindingVerified)
    {
        TransferDataHash = RestoreTransferDataHash(hash);
        HasVerifiedTransferDataHashBinding = bindingVerified && TransferDataHash is not null;
        _verifiedTransferDataPath = null;
    }

    protected void SetTransferDataHashBindingVerification(bool bindingVerified)
    {
        HasVerifiedTransferDataHashBinding = bindingVerified && IsValidTransferDataHash(TransferDataHash);
        _verifiedTransferDataPath = null;
    }

    protected void MarkTransferDataVerified(string path, string transferDataHash)
    {
        TransferDataHash = NormalizeTransferDataHash(transferDataHash);
        HasVerifiedTransferDataHashBinding = true;
        _verifiedTransferDataPath = Path.GetFullPath(path);
    }

    protected void SetUnverifiedTransferDataHash(string? transferDataHash)
    {
        TransferDataHash = RestoreTransferDataHash(transferDataHash);
        HasVerifiedTransferDataHashBinding = false;
        _verifiedTransferDataPath = null;
    }

    protected void ClearTransferDataHashBinding()
    {
        TransferDataHash = null;
        HasVerifiedTransferDataHashBinding = false;
        _verifiedTransferDataPath = null;
    }

    protected bool IsTransferDataVerified(string? path)
    {
        if (!HasVerifiedTransferDataHashBinding ||
            !IsValidTransferDataHash(TransferDataHash) ||
            string.IsNullOrEmpty(path) ||
            _verifiedTransferDataPath is null)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.GetFullPath(path),
            _verifiedTransferDataPath,
            comparison);
    }

    protected void MoveVerifiedTransferData(string sourcePath, string targetPath)
    {
        if (IsTransferDataVerified(sourcePath))
        {
            _verifiedTransferDataPath = Path.GetFullPath(targetPath);
        }
    }

    protected void CopyTransferDataHashStateTo(Profile target)
    {
        target.TransferDataHash = TransferDataHash;
        target.HasVerifiedTransferDataHashBinding = HasVerifiedTransferDataHashBinding;
        target._verifiedTransferDataPath = _verifiedTransferDataPath;
    }

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
        return Path.Combine(persistentDir, GetWorkingDirName(type, hash));
    }

    public static string GetWorkingDirName(ProfileType type, string hash)
    {
        if (hash.Contains(Path.DirectorySeparatorChar) || hash.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Hash contains invalid path characters.", nameof(hash));
        }

        return $"{type}_{hash}";
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
        return Create(dto, isTransferDataHashBindingVerified: false);
    }

    public static Profile Create(ProfileDto dto, bool isTransferDataHashBindingVerified)
    {
        Profile profile = dto.Type switch
        {
            ProfileType.Text => new TextProfile(dto),
            ProfileType.File => dto.DataName is not null && ImageTool.FileIsImage(dto.DataName)
                ? new ImageProfile(dto)
                : new FileProfile(dto),
            ProfileType.Image => new ImageProfile(dto),
            ProfileType.Group => new GroupProfile(dto),
            _ => throw new NotSupportedException($"Unsupported profile type from ProfileDto: {dto.Type}"),
        };
        profile.SetTransferDataHashBindingVerification(isTransferDataHashBindingVerified);
        return profile;
    }
}
