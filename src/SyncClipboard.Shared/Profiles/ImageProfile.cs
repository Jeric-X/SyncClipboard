using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Profiles;

public class ImageProfile : FileProfile
{
    public override ProfileType Type => ProfileType.Image;

    private IClipboardImage? _clipboardImage;
    private byte[]? _rawImageBytes;

    public ImageProfile(string? fullPath, string? fileName = null, string? hash = null)
        : base(fullPath, fileName, hash)
    {
    }

    public ImageProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    public ImageProfile(IClipboardImage clipboardImage) : base(null, CreateImageFileName())
    {
        _clipboardImage = clipboardImage;
    }

    private async Task<byte[]?> GetRawImageBytes(CancellationToken token)
    {
        if (_rawImageBytes is not null)
        {
            return _rawImageBytes;
        }

        if (_clipboardImage is not null)
        {
            _rawImageBytes = await _clipboardImage.SaveToBytes(token);
            _clipboardImage = null;
        }
        return _rawImageBytes;
    }

    public override async ValueTask<long> GetSize(CancellationToken token)
    {
        if (_size is not null)
        {
            return _size.Value;
        }

        var rawBytes = await GetRawImageBytes(token);
        if (rawBytes is not null)
        {
            _size = rawBytes.Length;
        }

        return await base.GetSize(token);
    }

    public override async ValueTask<string> GetHash(CancellationToken token)
    {
        if (_hash is not null)
        {
            return _hash;
        }

        var rawBytes = await GetRawImageBytes(token);
        if (rawBytes is not null)
        {
            _hash = await Utility.CalculateSHA256(rawBytes, token);
        }

        return await base.GetHash(token);
    }

    public override async Task PreparePersistent(CancellationToken token)
    {
        if (FullPath is not null && File.Exists(FullPath))
        {
            return;
        }
        var rawBytes = await GetRawImageBytes(token) ?? throw new InvalidOperationException("No image data available to prepare persistent storage.");
        var dir = await CreateWorkingDirectory(token);
        var filePath = Path.Combine(dir, FileName);
        await File.WriteAllBytesAsync(filePath, rawBytes, token);
        FullPath = filePath;
    }

    public override Task PrepareClipboard(CancellationToken token)
    {
        return PreparePersistent(token);
    }

    private static string CreateImageFileName()
    {
        return $"Image_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Path.GetRandomFileName()}.png";
    }
}