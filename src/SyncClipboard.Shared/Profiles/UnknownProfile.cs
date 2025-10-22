using SyncClipboard.Shared;

namespace SyncClipboard.Shared.Profiles;

public class UnknownProfile : Profile
{
    public override ProfileType Type => ProfileType.Unknown;

    private const string Text = "Unknown Clipboard";

    public override ValueTask<string> GetLogId(CancellationToken token)
    {
        return ValueTask.FromResult(Text);
    }

    protected override Task<bool> Same(Profile rhs, CancellationToken _)
    {
        return Task.FromResult(rhs is UnknownProfile);
    }

    public override string GetDisplayText()
    {
        return "Do not support this type of clipboard";
    }

    public override Task<ClipboardProfileDTO> ToDto(CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task<bool> IsLocalDataValid(bool quick, CancellationToken token)
    {
        return Task.FromResult(false);
    }
}
