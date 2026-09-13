using System.Text;
using SyncClipboard.Shared.Models;
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
        TransferDataHash = string.IsNullOrEmpty(entity.TransferDataFile) ? null : entity.TransferDataHash;
    }

    public TextProfile(ProfileDto dto)
    {
        _text = dto.Text;
        Hash = string.IsNullOrEmpty(dto.Hash) ? null : dto.Hash;
        _hasTransferData = dto.HasData;
        _transferDataName = dto.DataName;
        TransferDataHash = dto.TransferDataHash;
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
            TransferDataHash = _hasTransferData ? TransferDataHash : null,
            Size = await GetSize(token)
        };
    }

    public override async Task<bool> IsLocalDataValid(bool quick, CancellationToken token)
    {
        if (quick)
        {
            return _fullText is not null || !HasTransferData || File.Exists(_transferDataPath);
        }

        try
        {
            if (_fullText is not null || !HasTransferData)
            {
                var textHash = await Utility.CalculateSHA256(_fullText ?? _text, token);
                if (Hash is null || Utility.SHA256Same(textHash, Hash))
                {
                    Hash ??= textHash;
                    TransferDataHash = HasTransferData ? textHash : null;
                    return true;
                }
            }

            if (!File.Exists(_transferDataPath))
            {
                return false;
            }

            var actualHash = await Utility.CalculateFileSHA256(_transferDataPath!, token);
            if (Hash is not null && !Utility.SHA256Same(actualHash, Hash))
            {
                return false;
            }

            Hash ??= actualHash;
            TransferDataHash = actualHash;
            return true;
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

    public override Task<bool> IsDataComplete(bool quick, CancellationToken token)
    {
        return IsLocalDataValid(quick, token);
    }

    public override async Task<bool> TryLocalize(
        string localDir, bool clearInvalidLocalPaths = false, CancellationToken token = default)
    {
        if (!await IsDataComplete(false, token))
        {
            if (clearInvalidLocalPaths && !token.IsCancellationRequested)
            {
                _transferDataName ??= Path.GetFileName(_transferDataPath);
                _transferDataPath = null;
            }
            return false;
        }

        if (HasTransferData && _fullText is null)
        {
            _fullText = await File.ReadAllTextAsync(_transferDataPath!, Encoding.UTF8, token);
        }
        return true;
    }

    public override string? GetTransferDataSavePath(string persistentDir)
    {
        if (!HasTransferData)
        {
            return null;
        }

        var name = _transferDataName ?? Path.GetFileName(_transferDataPath) ?? $"{Type}_{Utility.CreateTimeBasedFileName()}.txt";
        return Path.Combine(QueryGetWorkingDir(persistentDir, Type, Hash ?? string.Empty), name);
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
        TransferDataHash = Utility.NormalizeRequiredSHA256(await GetHash(token));
        _fullText = null;
    }

    public override async Task<ProfilePersistentInfo> Persist(string persistentDir, CancellationToken token)
    {
        await WriteFullTextToFile(persistentDir, token);
        if (!HasTransferData || !File.Exists(_transferDataPath))
        {
            TransferDataHash = null;
        }
        var workingDir = QueryGetWorkingDir(persistentDir, Type, await GetHash(token));
        var path = GetPersistentPath(workingDir, _transferDataPath);
        return new ProfilePersistentInfo
        {
            Type = Type,
            Text = _text,
            Size = await GetSize(token),
            Hash = await GetHash(token),
            TransferDataFile = path,
            TransferDataHash = TransferDataHash,
            FilePaths = path is null ? [] : [path]
        };
    }

    public override async Task<FileHashInfo?> PrepareTransferData(string persistentDir, CancellationToken token)
    {
        var expectedHash = await GetHash(token);
        if (HasTransferData is false)
        {
            TransferDataHash = null;
            await ValidateInlineTextHashAsync(expectedHash, token);
            return null;
        }

        return await PrepareTransferFileAsync(persistentDir, expectedHash, token);
    }

    private async Task<FileHashInfo> PrepareTransferFileAsync(
        string persistentDir, string expectedHash, CancellationToken token)
    {
        await WriteFullTextToFile(persistentDir, token);
        var path = GetAvailableTransferDataPath();

        try
        {
            var hash = await ValidateTransferDataHashAsync(path, expectedHash, token);
            TransferDataHash = hash;
            return new FileHashInfo(path, hash);
        }
        catch (Exception ex) when (ShouldWrapLocalReadFailure(ex, token))
        {
            throw new LocalProfileDataUnavailableException(
                $"Failed to validate transfer data for Text profile {Hash ?? "<unknown>"}.", ex);
        }
    }

    private string GetAvailableTransferDataPath()
    {
        var path = _transferDataPath;
        if (path is null || !File.Exists(path))
        {
            throw new LocalProfileDataUnavailableException(
                $"Transfer data is unavailable for Text profile {Hash ?? "<unknown>"}.");
        }

        return path;
    }

    private async Task ValidateInlineTextHashAsync(string expectedHash, CancellationToken token)
    {
        var actualHash = await Utility.CalculateSHA256(_text, token);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalProfileDataUnavailableException(
                $"Text profile hash mismatch. Expected: {expectedHash}, Actual: {actualHash}.");
        }
    }

    private static async Task<string> ValidateTransferDataHashAsync(
        string path, string expectedHash, CancellationToken token)
    {
        var actualHash = await Utility.CalculateFileSHA256(path, token);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalProfileDataUnavailableException(
                $"Text transfer data hash mismatch. Expected: {expectedHash}, Actual: {actualHash}.");
        }

        return actualHash;
    }

    private static bool ShouldWrapLocalReadFailure(Exception ex, CancellationToken token)
    {
        return ex is IOException or UnauthorizedAccessException &&
               ex is not LocalProfileDataUnavailableException &&
               !token.IsCancellationRequested;
    }

    public override Task SetTransferData(string path, bool verify, CancellationToken token)
    {
        return SetTransferDataCore(path, null, verify, token);
    }

    public override Task SetTransferData(FileHashInfo file, bool verify, CancellationToken token)
    {
        return SetTransferDataCore(file.Path, file.Hash, verify, token);
    }

    private async Task SetTransferDataCore(string path, string? transferDataHash, bool verify, CancellationToken token)
    {
        EnsureTransferDataExists(path);
        if (transferDataHash is not null)
        {
            transferDataHash = Utility.NormalizeRequiredSHA256(transferDataHash);
        }
        else if (verify)
        {
            transferDataHash = await Utility.CalculateFileSHA256(path, token);
        }
        if (verify && Hash is not null && !Utility.SHA256Same(transferDataHash, Hash))
        {
            throw new InvalidOperationException("Transfer data file content does not match the text hash.");
        }

        Hash ??= transferDataHash;
        TransferDataHash = transferDataHash;
        _transferDataPath = path;
        _transferDataName = Path.GetFileName(path);
    }

    private static void EnsureTransferDataExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Text transfer data file does not exist: {path}", path);
        }
    }

    public override Task SetAndMoveTransferData(string persistentDir, string path, CancellationToken token)
    {
        return SetAndMoveTransferDataCore(persistentDir, path, null, token);
    }

    public override Task SetAndMoveTransferData(string persistentDir, FileHashInfo file, CancellationToken token)
    {
        return SetAndMoveTransferDataCore(persistentDir, file.Path, file.Hash, token);
    }

    private async Task SetAndMoveTransferDataCore(
        string persistentDir, string path, string? transferDataHash, CancellationToken token)
    {
        await SetTransferDataCore(path, transferDataHash, true, token);

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

    public override async Task<ProfileLocalInfo> Localize(string _, CancellationToken token)
    {
        if (_fullText is not null)
        {
            return new ProfileLocalInfo { Text = _fullText };
        }

        if (HasTransferData is false)
        {
            return new ProfileLocalInfo { Text = _text };
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
        textTarget.TransferDataHash = TransferDataHash;
    }
}
