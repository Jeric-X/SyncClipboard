using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Profiles;

public class ImageProfile : FileProfile
{
    public override ProfileType Type => ProfileType.Image;

    private IClipboardImage? _clipboardImage;
    private byte[]? _rawImageBytes;
    private readonly SemaphoreSlim _rawImageLock = new(1, 1);

    public ImageProfile(ProfilePersistentInfo entity) : base(entity)
    {
    }

    public ImageProfile(string? fullPath, string? fileName = null, string? hash = null)
        : base(fullPath, fileName, hash)
    {
    }

    public ImageProfile(ProfileDto dto) : base(dto)
    {
    }

    private async Task<byte[]?> GetRawImageBytes(CancellationToken token)
    {
        if (_rawImageBytes is not null)
        {
            return _rawImageBytes;
        }

        await _rawImageLock.WaitAsync(token);
        try
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
        finally
        {
            _rawImageLock.Release();
        }
    }

    protected override async Task ComputeHash(CancellationToken token)
    {
        var rawBytes = await GetRawImageBytes(token);
        if (rawBytes is not null)
        {
            var contentHash = await Utility.CalculateSHA256(rawBytes, token);
            Hash = await CombineHash(FileName, contentHash, token);
        }
        else
        {
            await base.ComputeHash(token);
        }
    }

    protected override async Task ComputeSize(CancellationToken token)
    {
        var rawBytes = await GetRawImageBytes(token);
        if (rawBytes is not null)
        {
            Size = rawBytes.Length;
        }
        else
        {
            await base.ComputeSize(token);
        }
    }

    private async Task WriteRawImageBytesToFile(string persistentDir, bool exception, CancellationToken token)
    {
        await _persistentLock.WaitAsync(token);
        using var guard = new ScopeGuard(() => _persistentLock.Release());

        if (FullPath is not null && File.Exists(FullPath))
        {
            return;
        }

        var rawBytes = await GetRawImageBytes(token);
        if (rawBytes is null)
        {
            if (exception)
                throw new InvalidOperationException("No image data available to prepare persistent storage.");
            return;
        }
        var workingDir = GetWorkingDir(persistentDir, Type, await GetHash(token));
        var filePath = Path.Combine(workingDir, FileName);
        await File.WriteAllBytesAsync(filePath, rawBytes, token);
        FullPath = filePath;
    }

    private readonly SemaphoreSlim _persistentLock = new(1, 1);
    private async Task PrepareIfRawImageExist(string persistentDir, CancellationToken token)
    {
        if (FullPath is not null && File.Exists(FullPath))
        {
            return;
        }
        await WriteRawImageBytesToFile(persistentDir, true, token);
    }

    public override async Task<string?> PrepareTransferData(string persistentDir, CancellationToken token)
    {
        await PrepareIfRawImageExist(persistentDir, token);
        return await base.PrepareTransferData(persistentDir, token);
    }

    public static string CreateImageFileName()
    {
        return $"Image_{Utility.CreateTimeBasedFileName()}.png";
    }

    public override async Task<ProfilePersistentInfo> Persist(string persistentDir, CancellationToken token)
    {
        if (FullPath is null)
        {
            await PrepareIfRawImageExist(persistentDir, token);
        }

        return await base.Persist(persistentDir, token);
    }

    public override async Task<ProfileLocalInfo> Localize(string persistentDir, bool quick, CancellationToken token)
    {
        if (FullPath is null)
        {
            await PrepareIfRawImageExist(persistentDir, token);
        }

        return await base.Localize(persistentDir, quick, token);
    }
}