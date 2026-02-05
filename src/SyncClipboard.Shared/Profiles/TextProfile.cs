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
        _hasTransferData = !string.IsNullOrEmpty(entity.TransferDataFile) || entity.Size > _text.Length;
        if (_hasTransferData)
        {
            _transferDataPath = entity.TransferDataFile;
            if (_transferDataPath is null && entity.FilePaths.Length > 0)
            {
                _transferDataPath = entity.FilePaths[0];
            }
        }
        Size = entity.Size;
        Hash = string.IsNullOrEmpty(entity.Hash) ? null : entity.Hash;
    }

    public TextProfile(ProfileDto dto)
    {
        _text = dto.Text;
        Hash = string.IsNullOrEmpty(dto.Hash) ? null : dto.Hash;
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
        catch when (token.IsCancellationRequested is false)
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

    public override async Task<string?> NeedsTransferData(string persistentDir, CancellationToken token)
    {
        if (await IsLocalDataValid(false, token))
        {
            return null;
        }

        if (_transferDataPath is not null && File.Exists(_transferDataPath))
        {
            try
            {
                await SetTransferData(_transferDataPath, true, token);
                return null;
            }
            catch when (token.IsCancellationRequested is false)
            { }
        }

        _transferDataName ??= $"{Type}_{Utility.CreateTimeBasedFileName()}.txt";
        var dataPath = Path.Combine(CreateWorkingDir(persistentDir, await GetHash(token)), _transferDataName);
        return dataPath;
    }

    private readonly SemaphoreSlim _persistentLock = new(1, 1);
    private async Task WriteFullTextToFile(string persistentDir, CancellationToken token)
    {
        if (!HasTransferData || File.Exists(_transferDataPath) is true || _fullText is null)
        {
            return;
        }

        await _persistentLock.WaitAsync(token);
        using var guard = new ScopeGuard(() => _persistentLock.Release());

        if (!HasTransferData || File.Exists(_transferDataPath) is true || _fullText is null)
        {
            return;
        }

        var workingDir = CreateWorkingDir(persistentDir, Type, await GetHash(token));
        var dataName = _transferDataName ?? $"{Type}_{Utility.CreateTimeBasedFileName()}.txt";
        var path = Path.Combine(workingDir, dataName);
        await File.WriteAllTextAsync(path, _fullText, new UTF8Encoding(false), token);
        _transferDataPath = path;
        _transferDataName = dataName;
        _fullText = null;
    }

    public override async Task<ProfilePersistentInfo> Persist(string persistentDir, CancellationToken token)
    {
        await WriteFullTextToFile(persistentDir, token);
        var workingDir = QueryGetWorkingDir(persistentDir, Type, await GetHash(token));
        var path = GetPersistentPath(workingDir, _transferDataPath);
        return new ProfilePersistentInfo
        {
            Type = Type,
            Text = _text,
            Size = await GetSize(token),
            Hash = await GetHash(token),
            TransferDataFile = path,
            FilePaths = path is null ? [] : [path]
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

    public override async Task SetTransferData(string path, bool verify, CancellationToken token)
    {
        if (verify && File.Exists(path) && Hash is not null)
        {
            var fileHash = await Utility.CalculateFileSHA256(path, token);
            var textHash = Hash;

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

        await SetTransferData(path, true, token);

        var workingDir = CreateWorkingDir(persistentDir, Type, Hash!);
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

    public override async Task<ProfileLocalInfo> Localize(string _, bool quick, CancellationToken token)
    {
        if (HasTransferData is false || quick)
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

    public override void CopyTo(Profile target)
    {
        if (target is not TextProfile textTarget)
            return;

        textTarget._fullText = _fullText;
        textTarget._transferDataPath = _transferDataPath;
        textTarget._transferDataName = _transferDataName;
        textTarget.Hash = Hash;
        textTarget.Size = Size;
    }
}