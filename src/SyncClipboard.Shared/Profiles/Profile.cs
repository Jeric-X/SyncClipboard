using System.Diagnostics.CodeAnalysis;

namespace SyncClipboard.Shared.Profiles;

public abstract class Profile
{
    public abstract ProfileType Type { get; }
    public abstract string Text { get; }
    public abstract string GetDisplayText();
    public abstract ValueTask<string> GetLogId(CancellationToken token);
    public abstract Task<bool> IsLocalDataValid(bool quick, CancellationToken token);
    public abstract Task<ClipboardProfileDTO> ToDto(CancellationToken token);
    public abstract ValueTask<long> GetSize(CancellationToken token);
    public abstract ValueTask<string> GetHash(CancellationToken token);
    public virtual Task PreparePersistent(CancellationToken token) => Task.CompletedTask;
    public virtual Task PrepareClipboard(CancellationToken token) => Task.CompletedTask;

    public virtual bool HasTransferData => false;
    public virtual string? TransferDataPath { get; protected set; } = null;
    public virtual Task<string?> PrepareTransferData(CancellationToken token) => Task.FromResult<string?>(null);
    public virtual Task SetTranseferData(string path, bool verify, CancellationToken token) => Task.CompletedTask;
    public abstract bool NeedsTransferData([NotNullWhen(true)] out string? dataPath);

    protected abstract Task<bool> Same(Profile rhs, CancellationToken token);

    public async Task<string> GetProfileId(CancellationToken token)
    {
        return $"{Type}-{await GetHash(token)}";
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

    public static Task<bool> Same(Profile? lhs, Profile? rhs, CancellationToken token)
    {
        if (ReferenceEquals(lhs, rhs))
        {
            return Task.FromResult(true);
        }

        if (lhs is null)
        {
            return Task.FromResult(rhs is null);
        }

        if (rhs is null)
        {
            return Task.FromResult(false);
        }

        if (lhs.GetType() != rhs.GetType())
        {
            return Task.FromResult(false);
        }

        return lhs.Same(rhs, token);
    }

    public override bool Equals(object? obj)
    {
        throw new NotSupportedException("Use Profile.Same to compare two profiles.");
    }

    public override int GetHashCode()
    {
        throw new NotImplementedException();
    }

    private static IProfileEnv? _profileWorkingDirProvider = null;

    public static void SetGlobalProfileEnvProvider(IProfileEnv provider)
    {
        _profileWorkingDirProvider = provider;
    }

    protected async Task<string> CreateWorkingDirectory(CancellationToken token)
    {
        var provider = _profileWorkingDirProvider ?? throw new InvalidOperationException("Profile working directory provider is not set.");
        var dirName = $"{Type}_{await GetHash(token)}";
        var allProfileDir = provider.GetWorkingDir();
        var profileDir = Path.Combine(allProfileDir, dirName);
        if (!Directory.Exists(profileDir))
            Directory.CreateDirectory(profileDir);
        return profileDir;
    }

    protected string CreateWorkingDirectory(string hash)
    {
        var provider = _profileWorkingDirProvider ?? throw new InvalidOperationException("Profile working directory provider is not set.");
        var dirName = $"{Type}_{hash}";
        var allProfileDir = provider.GetWorkingDir();
        var profileDir = Path.Combine(allProfileDir, dirName);
        if (!Directory.Exists(profileDir))
            Directory.CreateDirectory(profileDir);
        return profileDir;
    }
}
