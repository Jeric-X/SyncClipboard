using SyncClipboard.Abstract;

namespace SyncClipboard.Core.Clipboard;

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

    public override string ShowcaseText()
    {
        return "Do not support this type of clipboard";
    }

    public override Task<ClipboardProfileDTO> ToDto(CancellationToken token)
    {
        throw new NotImplementedException();
    }
}