using SyncClipboard.Shared.Profiles.Models;

namespace SyncClipboard.Shared.Profiles;

public class UnknownProfile : Profile
{
    public override ProfileType Type => ProfileType.Unknown;

    public override bool HasTransferData => false;

    public override string ShortDisplayText => "Do not support this type of clipboard";

    public override string DisplayText => "Do not support this type of clipboard";

    public override Task<ProfileDto> ToProfileDto(CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task<bool> IsLocalDataValid(bool quick, CancellationToken token)
    {
        return Task.FromResult(false);
    }

    protected override Task ComputeHash(CancellationToken token)
    {
        Hash = "UNKNOWN_PROFILE_HASH";
        return Task.CompletedTask;
    }

    protected override Task ComputeSize(CancellationToken token)
    {
        Size = 0;
        return Task.CompletedTask;
    }

    public override Task<string?> NeedsTransferData(string persistentDir, CancellationToken token)
    {
        return Task.FromResult<string?>(null);
    }

    public override Task<ProfilePersistentInfo> Persist(string persistentDir, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task<ProfileLocalInfo> Localize(string persistentDir, bool quick, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task<string?> PrepareTransferData(string persistentDir, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task SetTransferData(string path, bool verify, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task SetAndMoveTransferData(string persistentDir, string path, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override void CopyTo(Profile target)
    {
    }
}
