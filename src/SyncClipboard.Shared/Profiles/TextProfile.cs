using System.Diagnostics.CodeAnalysis;
using System.Text;
using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Profiles;

public class TextProfile : Profile
{
    private const int TRANSFER_DATA_THRESHOLD = 10240;

    public override ProfileType Type => ProfileType.Text;
    public override string DisplayText => _text;
    public override string ShortDisplayText => GetShortDisplayText();
    public override bool HasTransferData => _hasTransferData;
    private readonly bool _hasTransferData = false;
    private string? _transferDataPath;
    private string? _transferDataName;
    private readonly string _text;
    private string? _fullText;

    public TextProfile(string text)
    {
        Size = text.Length;
        if (Size > TRANSFER_DATA_THRESHOLD)
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
        _hasTransferData = !string.IsNullOrEmpty(entity.TransferDataFile) || entity.Size > TRANSFER_DATA_THRESHOLD;
        _transferDataPath = _hasTransferData ? entity.TransferDataFile : null;
        Size = entity.Size;
        Hash = entity.Hash;
    }

    public TextProfile(ProfileDto dto)
    {
        _text = dto.Text;
        Hash = dto.Hash;
        _hasTransferData = dto.HasData;
        _transferDataName = dto.DataName;
        Size = dto.Size;
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

    public override async Task<ProfileDto> ToProfileDto(CancellationToken token)
    {
        return new ProfileDto
        {
            Type = Type,
            Hash = await GetHash(token),
            Text = _text,
            HasData = _hasTransferData,
            DataName = _hasTransferData ? _transferDataName ?? Path.GetFileName(_transferDataPath) : null,
            Size = await GetSize(token)
        };
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

    protected override async Task ComputeHash(CancellationToken token)
    {
        if (_fullText is not null)
        {
            Hash = await Utility.CalculateSHA256(_fullText, token);
            return;
        }

        if (HasTransferData)
        {
            if (File.Exists(_transferDataPath))
            {
                Hash = await Utility.CalculateFileSHA256(_transferDataPath, token);
            }
            return;
        }

        Hash = await Utility.CalculateSHA256(_text, token);
    }

    protected override async Task ComputeSize(CancellationToken token)
    {
        if (_fullText is not null)
        {
            Size = _fullText.Length;
            return;
        }

        if (HasTransferData)
        {
            if (File.Exists(_transferDataPath))
            {
                var fullText = await File.ReadAllTextAsync(_transferDataPath, Encoding.UTF8, token);
                Size = fullText.Length;
            }
            return;
        }

        Size = _text.Length;
    }

    public override bool NeedsTransferData(string persistentDir, [NotNullWhen(true)] out string? dataPath)
    {
        dataPath = null;
        if (HasTransferData is false)
        {
            return false;
        }

        if (File.Exists(_transferDataPath) is false)
        {
            _transferDataName ??= Utility.CreateTimeBasedFileName();
            var fileName = $"{Type}_{_transferDataName}.txt";
            dataPath = Path.Combine(GetWorkingDir(persistentDir, Hash ?? string.Empty), fileName);
            return true;
        }
        return false;
    }

    private async Task WriteFullTextToFile(string persistentDir, CancellationToken token)
    {
        if (HasTransferData && File.Exists(_transferDataPath) is false && _fullText is not null)
        {
            var workingDir = GetWorkingDir(persistentDir, Type, await GetHash(token));
            var dataName = _transferDataName ?? Utility.CreateTimeBasedFileName();
            var path = Path.Combine(workingDir, $"{Type}_{dataName}.txt");
            await File.WriteAllTextAsync(path, _fullText, Encoding.UTF8, token);
            _transferDataPath = path;
            _transferDataName = dataName;
            _fullText = null;
        }
    }

    public override async Task<ProfilePersistentInfo> Persist(string persistentDir, CancellationToken token)
    {
        await WriteFullTextToFile(persistentDir, token);
        return new ProfilePersistentInfo
        {
            Type = Type,
            Text = _text,
            Size = await GetSize(token),
            Hash = await GetHash(token),
            TransferDataFile = GetPersistentPath(GetWorkingDir(persistentDir, Type, await GetHash(token)), _transferDataPath),
        };
    }

    public override async Task<string?> PrepareTransferData(string persistentDir, CancellationToken token)
    {
        if (HasTransferData is false)
        {
            return null;
        }

        await WriteFullTextToFile(persistentDir, token);
        if (_transferDataPath is null)
        {
            throw new Exception($"Can not prepare transfer data for {_text}");
        }

        return _transferDataPath;
    }

    public override async Task SetTranseferData(string path, bool verify, CancellationToken token)
    {
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
        _transferDataName = Path.GetFileName(path);
    }

    public override async Task SetAndMoveTransferData(string persistentDir, string path, CancellationToken token)
    {
        if (File.Exists(_transferDataPath))
        {
            return;
        }

        await SetTranseferData(path, true, token);

        var workingDir = GetWorkingDir(persistentDir, Type, Hash!);
        var persistentPath = GetPersistentPath(workingDir, path);

        if (Path.IsPathRooted(persistentPath!) is false)
        {
            return;
        }

        var fileName = Path.GetFileName(path);

        var targetPath = Path.Combine(workingDir, fileName);
        File.Move(path, targetPath, true);
        _transferDataPath = targetPath;
        _transferDataName = Path.GetFileName(targetPath);
    }

    public override async Task<ProfileLocalInfo> Localize(string _, CancellationToken token)
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