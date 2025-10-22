using SyncClipboard.Shared;

namespace SyncClipboard.Shared.Profiles;

public class TextProfile(string text) : Profile
{
    public string Text { get; set; } = text;

    public override ProfileType Type => ProfileType.Text;

    public override ValueTask<string> GetLogId(CancellationToken token)
    {
        return ValueTask.FromResult(Text);
    }

    public override string GetDisplayText()
    {
        if (Text.Length > 500)
        {
            return Text[..500] + "\n...";
        }
        return Text;
    }

    protected override Task<bool> Same(Profile rhs, CancellationToken _)
    {
        try
        {
            var textprofile = (TextProfile)rhs;
            return Task.FromResult(Text == textprofile.Text);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public override Task<ClipboardProfileDTO> ToDto(CancellationToken token) => Task.FromResult(new ClipboardProfileDTO(string.Empty, Text, Type));

    public override Task<bool> IsLocalDataValid(bool quick, CancellationToken token)
    {
        return Task.FromResult(true);
    }
}
