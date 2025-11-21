using System.Diagnostics.CodeAnalysis;
using System.Text;
using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Profiles;

public class TextProfile : Profile
{
    private const int TRANSFER_DATA_THRESHOLD = 102400;

    public override ProfileType Type => ProfileType.Text;
    public override string DisplayText => _text;
    public override string ShortDisplayText => GetShortDisplayText();
    public override bool HasTransferData => _hasTransferData;
    private string? _hash = null;
    private long _size;
    private readonly bool _hasTransferData = false;
    private string? _transferDataPath;
    private readonly string _text;
    private string? _fullText;

    public TextProfile(string text)
    {
        _size = text.Length;
        if (_size > TRANSFER_DATA_THRESHOLD)
        {
            _fullText = text;
            _text = text[0..TRANSFER_DATA_THRESHOLD];
            _hasTransferData = true;
        }
        else
        {
            _text = text;
        }
    }

    public TextProfile(ProfilePersistentInfo entity)
    {
        _text = entity.Text;
        _hasTransferData = !string.IsNullOrEmpty(entity.TransferDataFile);
        _transferDataPath = _hasTransferData ? entity.TransferDataFile : null;
        _size = entity.Size;
        _hash = entity.Hash;
    }

    public string GetShortDisplayText()
    {
        if (_text.Length > 500)
        {
            return _text[..500] + "\n...";
        }
        return _text;
    }

    public override async Task<ClipboardProfileDTO> ToDto(CancellationToken token)
    {
        if (_hasTransferData)
        {
            if (_fullText is not null)
            {
                return new ClipboardProfileDTO(string.Empty, _fullText, Type);
            }
            else if (File.Exists(_transferDataPath))
            {
                var fullText = await File.ReadAllTextAsync(_transferDataPath, token);
                return new ClipboardProfileDTO(string.Empty, fullText, Type);
            }
            throw new Exception("Text profile data is not ready.");
        }
        return new ClipboardProfileDTO(string.Empty, _text, Type);
    }

    public override async Task<bool> IsLocalDataValid(bool quick, CancellationToken token)
    {
        if (!HasTransferData)
        {
            return true;
        }

        if (File.Exists(_transferDataPath) is false)
        {
            return false;
        }

        if (quick)
        {
            return true;
        }

        try
        {
            var hash = await GetHash(token);
            await using var file = new FileStream(_transferDataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var sha256Hex = await Utility.CalculateSHA256(file, token);
            return string.Equals(hash, sha256Hex, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public override async ValueTask<string> GetHash(CancellationToken token)
    {
        if (_hash is not null)
        {
            return _hash;
        }

        if (_fullText is not null)
        {
            _hash = await Utility.CalculateSHA256(_fullText, token);
            return _hash;
        }

        if (HasTransferData && File.Exists(_transferDataPath))
        {
            _hash = await Utility.CalculateFileSHA256(_transferDataPath, token);
            return _hash;
        }

        return string.Empty;
    }

    public override async ValueTask<long> GetSize(CancellationToken token)
    {
        if (_size != 0)
        {
            return _size;
        }

        if (_fullText is not null)
        {
            _size = _fullText.Length;
            return _size;
        }

        if (HasTransferData && File.Exists(_transferDataPath))
        {
            var fullText = await File.ReadAllTextAsync(_transferDataPath, Encoding.UTF8, token);
            _size = fullText.Length;
            return _size;
        }

        _size = _text.Length;
        return _size;
    }

    public override bool NeedsTransferData([NotNullWhen(true)] out string? dataPath)
    {
        dataPath = null;
        if (HasTransferData is false)
        {
            return false;
        }

        if (_hash is not null && File.Exists(_transferDataPath) is false)
        {
            dataPath = $"{Type}_{Utility.CreateTimeBasedFileName()}.txt";
            return true;
        }
        return false;
    }

    private async Task WriteFullTextToFile(CancellationToken token)
    {
        var workingDir = await GetWorkingDirectory(token);
        if (HasTransferData && File.Exists(_transferDataPath) is false && _fullText is not null)
        {
            var path = Path.Combine(workingDir, $"{Type}_{Utility.CreateTimeBasedFileName()}.txt");
            await File.WriteAllTextAsync(path, _fullText, Encoding.UTF8, token);
            _transferDataPath = path;
            _fullText = null;
        }
    }

    public override async Task<ProfilePersistentInfo> Persistentize(CancellationToken token)
    {
        await WriteFullTextToFile(token);
        return new ProfilePersistentInfo
        {
            Type = Type,
            Text = _text,
            Size = await GetSize(token),
            Hash = await GetHash(token),
            TransferDataFile = GetPersistentPath(await GetWorkingDirectory(token), _transferDataPath),
        };
    }

    public override async Task<string?> PrepareTransferData(CancellationToken token)
    {
        if (HasTransferData is false)
        {
            return null;
        }

        await WriteFullTextToFile(token);
        if (_transferDataPath is null)
        {
            throw new Exception($"Can not prepare transfer data for {_text}");
        }

        return _transferDataPath;
    }

    public override async Task SetTranseferData(string path, bool verify, CancellationToken token)
    {
        if (!_hasTransferData)
        {
            throw new InvalidOperationException("SetTranseferData should only be called when HasTransferData is true.");
        }

        if (verify && File.Exists(path))
        {
            var fileHash = await Utility.CalculateFileSHA256(path, token);
            var textHash = await GetHash(token);

            if (!string.Equals(fileHash, textHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Transfer data file content does not match the text hash.");
            }
        }

        _transferDataPath = path;
    }

    public override async Task<ProfileLocalInfo> Localize(CancellationToken token)
    {
        if (HasTransferData is false)
        {
            return new ProfileLocalInfo { Text = _text };
        }

        if (_fullText is not null)
        {
            return new ProfileLocalInfo { Text = _fullText };
        }

        if (File.Exists(_transferDataPath))
        {
            return new ProfileLocalInfo { Text = await File.ReadAllTextAsync(_transferDataPath, Encoding.UTF8, token) };
        }

        throw new Exception("Text profile data lost.");
    }
}