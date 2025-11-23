using System.Diagnostics.CodeAnalysis;
using SyncClipboard.Shared.Profiles.Models;

namespace SyncClipboard.Shared.Profiles;

public class UnknownProfile : Profile
{
    public override ProfileType Type => ProfileType.Unknown;

    public override bool HasTransferData => false;

    public override string ShortDisplayText => "Do not support this type of clipboard";

    public override string DisplayText => "Do not support this type of clipboard";

    public override Task<ClipboardProfileDTO> ToDto(CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task<bool> IsLocalDataValid(bool quick, CancellationToken token)
    {
        return Task.FromResult(false);
    }

    public override ValueTask<string> GetHash(CancellationToken token)
    {
        return ValueTask.FromResult("UNKNOWN_PROFILE_HASH");
    }

    public override ValueTask<long> GetSize(CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override bool NeedsTransferData(string persistentDir, [NotNullWhen(true)] out string? dataPath)
    {
        dataPath = null;
        return false;
    }

    public override Task<ProfilePersistentInfo> Persist(string persistentDir, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task<ProfileLocalInfo> Localize(string persistentDir, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task<string?> PrepareTransferData(string persistentDir, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task SetTranseferData(string path, bool verify, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task SetAndMoveTransferData(string persistentDir, string path, CancellationToken token)
    {
        throw new NotImplementedException();
    }
}
