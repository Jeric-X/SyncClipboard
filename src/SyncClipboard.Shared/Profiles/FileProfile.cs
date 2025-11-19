using System.Security.Cryptography;
using System.Text;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Profiles;

public class FileProfile : Profile
{
    protected const long MAX_FILE_SIZE = int.MaxValue;

    public virtual string FileName { get; set; } = "";
    public override ProfileType Type => ProfileType.File;
    public virtual string? FullPath { get; set; }

    public override string Text => FileName;

    protected string? _hash;
    protected long? _size;

    protected const string HASH_FOR_OVERSIZED_FILE = "HASH_FOR_OVERSIZED_FILE";

    public FileProfile(string? fullPath, string? fileName = null, string? hash = null)
    {
        if (fullPath is null && fileName is null)
        {
            throw new ArgumentNullException(nameof(fullPath), "Either fullPath or fileName must be provided.");
        }

        if (fullPath is not null)
        {
            FileName = Path.GetFileName(fullPath);
        }
        else if (fileName is not null)
        {
            FileName = fileName;
        }

        FullPath = fullPath;
        _hash = hash;
    }

    public FileProfile(ClipboardProfileDTO profileDTO) : this(null, profileDTO.File, profileDTO.Clipboard)
    {
    }

    public override ValueTask<long> GetSize(CancellationToken token)
    {
        if (_size is null)
        {
            if (FullPath is null || !File.Exists(FullPath))
            {
                return ValueTask.FromResult(0L);
            }

            var fileInfo = new FileInfo(FullPath);
            _size = fileInfo.Length;
        }
        return ValueTask.FromResult(_size.Value);
    }

    public override async ValueTask<string> GetHash(CancellationToken token)
    {
        if (_hash is null)
        {
            if (FullPath is null)
            {
                return string.Empty;
            }

            _hash = await GetSHA256HashFromFile(FullPath, token); ;
        }

        return _hash;
    }

    public override ValueTask<string> GetLogId(CancellationToken token)
    {
        return GetHash(token);
    }

    public override async Task<ClipboardProfileDTO> ToDto(CancellationToken token) => new ClipboardProfileDTO(FileName, await GetHash(token), Type);

    public override string GetDisplayText()
    {
        return FileName;
    }

    protected override async Task<bool> Same(Profile rhs, CancellationToken token)
    {
        try
        {
            var hashThisTask = GetHash(token);
            var hashOtherTask = ((FileProfile)rhs).GetHash(token);
            var hashThis = await hashThisTask;
            var hashOther = await hashOtherTask;
            if (string.IsNullOrEmpty(hashThis) || string.IsNullOrEmpty(hashOther))
            {
                return false;
            }
            return string.Equals(hashThis, hashOther, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    protected async static Task<string> GetSHA256HashFromFile(string fileName, CancellationToken? cancelToken)
    {
        cancelToken ??= CancellationToken.None;
        var fileInfo = new FileInfo(fileName);
        if (fileInfo.Length > MAX_FILE_SIZE)
        {
            return HASH_FOR_OVERSIZED_FILE;
        }

        await using var file = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var sha256Hex = await Utility.CalculateSHA256(file, cancelToken.Value);
        return sha256Hex;
    }

    public virtual async Task SetTranseferData(string path, CancellationToken token)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File does not exist: {path}", path);
        }

        ArgumentNullException.ThrowIfNull(_hash);

        var hash = await GetSHA256HashFromFile(path, token);
        if (hash != _hash)
        {
            throw new InvalidDataException(path);
        }
        FullPath = path;
        FileName = Path.GetFileName(path);
    }

    public override async Task<bool> IsLocalDataValid(bool quick, CancellationToken token)
    {
        if (string.IsNullOrEmpty(FullPath))
            return false;

        if (!File.Exists(FullPath))
            return false;

        if (quick)
            return true;

        if (_hash is null)
        {
            return true;
        }

        try
        {
            var hash = await GetSHA256HashFromFile(FullPath, token);
            return hash == _hash;
        }
        catch
        {
            return false;
        }
    }
}
