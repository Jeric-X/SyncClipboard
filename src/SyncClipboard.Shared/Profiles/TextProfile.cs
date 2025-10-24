using System.Security.Cryptography;
using System.Text;

namespace SyncClipboard.Shared.Profiles;

public class TextProfile(string text) : Profile
{
    private string? _hash = null;
    private long? _size;
    public override string Text { get; } = text;

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

    public override async ValueTask<string> GetHash(CancellationToken token)
    {
        if (_hash is null)
        {
            byte[] inputBytes = Encoding.Unicode.GetBytes(Text);
            using var ms = new MemoryStream(inputBytes);
            var hashBytes = await SHA256.HashDataAsync(ms, token);
            _hash = Convert.ToHexString(hashBytes);
        }

        return _hash;
    }

    public override ValueTask<long> GetSize(CancellationToken token)
    {
        _size ??= Text.Length;
        return ValueTask.FromResult(_size.Value);
    }
}