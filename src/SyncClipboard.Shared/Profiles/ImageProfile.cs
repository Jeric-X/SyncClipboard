using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Profiles;

public class ImageProfile : FileProfile
{
    public override ProfileType Type => ProfileType.Image;

    private IClipboardImage? _clipboardImage;
    private byte[]? _rawImageBytes;

    public ImageProfile(ProfilePersistentInfo entity) : base(entity)
    {
    }

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

    private async Task WriteRawImageBytesToFile(bool exception, CancellationToken token)
    {
        var rawBytes = await GetRawImageBytes(token);
        if (rawBytes is null)
        {
            if (exception)
                throw new InvalidOperationException("No image data available to prepare persistent storage.");
            return;
        }
        var dir = await GetWorkingDirectory(token);
        var filePath = Path.Combine(dir, FileName);
        await File.WriteAllBytesAsync(filePath, rawBytes, token);
        FullPath = filePath;
    }

    private Task PreparePersistent(CancellationToken token)
    {
        if (FullPath is not null && File.Exists(FullPath))
        {
            return Task.CompletedTask;
        }
        return WriteRawImageBytesToFile(true, token);
    }

    public override async Task<string?> PrepareTransferData(CancellationToken token)
    {
        if (FullPath is null || File.Exists(FullPath) is false)
        {
            await WriteRawImageBytesToFile(false, token);
        }

        return await base.PrepareTransferData(token);
    }

    private static string CreateImageFileName()
    {
        return $"Image_{Utility.CreateTimeBasedFileName()}.png";
    }

    public override async Task<ProfilePersistentInfo> Persistentize(CancellationToken token)
    {
        if (FullPath is null)
        {
            await PreparePersistent(token);
        }

        return await base.Persistentize(token);
    }

    public override async Task<ProfileLocalInfo> Localize(CancellationToken token)
    {
        if (FullPath is null)
        {
            await PreparePersistent(token);
        }

        return await base.Localize(token);
    }
}