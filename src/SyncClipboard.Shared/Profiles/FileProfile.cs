using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Profiles;

public class FileProfile : Profile
{
    public virtual string FileName { get; set; } = "";
    public override string DisplayText => FileName;

    public override string ShortDisplayText => FileName;
    public override ProfileType Type => ProfileType.File;
    public virtual string? FullPath { get; set; }
    public override bool HasTransferData => true;

    public FileProfile(ProfilePersistentInfo entity)
    {
        if (entity.FilePaths.Length > 0)
        {
            FullPath = entity.FilePaths[0];
        }
        else if (string.IsNullOrEmpty(entity.TransferDataFile) is false)
        {
            FullPath = entity.TransferDataFile;
        }
        FileName = entity.Text;
        Hash = string.IsNullOrEmpty(entity.Hash) ? null : entity.Hash;
        RestoreTransferDataHashBinding(entity.TransferDataHash, bindingVerified: true);
    }

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
        Hash = string.IsNullOrEmpty(hash) ? null : hash;
    }

    public FileProfile(ProfileDto dto) : this(null, dto.DataName, dto.Hash)
    {
        Size = dto.Size;
        RestoreTransferDataHashBinding(dto.TransferDataHash, bindingVerified: false);
    }

    protected override async Task ComputeHash(CancellationToken token)
    {
        if (FullPath is null || !File.Exists(FullPath))
        {
            return;
        }

        var hashes = await GetHashesFromFile(FullPath, token);
        Hash = hashes.ProfileHash;
        MarkTransferDataVerified(FullPath, hashes.TransferDataHash);
    }

    protected override Task ComputeSize(CancellationToken token)
    {
        if (FullPath is null || !File.Exists(FullPath))
        {
            return Task.CompletedTask;
        }

        var fileInfo = new FileInfo(FullPath);
        Size = fileInfo.Length;
        return Task.CompletedTask;
    }

    public override async Task<ProfileDto> ToProfileDto(CancellationToken token)
    {
        return new ProfileDto
        {
            Type = Type,
            Hash = await GetHash(token),
            Text = FileName,
            HasData = true,
            DataName = FileName,
            TransferDataHash = HasVerifiedTransferDataHashBinding ? TransferDataHash : null,
            Size = await GetSize(token)
        };
    }

    protected async static Task<string> CombineHash(string fileName, string contentHash, CancellationToken token)
    {
        var combinedString = $"{fileName}|{contentHash.ToUpperInvariant()}";
        var hash = await Utility.CalculateSHA256(combinedString, token);
        return hash;
    }

    protected async static Task<string> GetSHA256HashFromFile(string filePath, CancellationToken? cancelToken)
    {
        cancelToken ??= CancellationToken.None;
        return (await GetHashesFromFile(filePath, cancelToken.Value)).ProfileHash;
    }

    private protected async static Task<(string ProfileHash, string TransferDataHash)> GetHashesFromFile(
        string filePath,
        CancellationToken token)
    {
        var contentSha256Hex = await Utility.CalculateFileSHA256(filePath, token);
        var fileName = Path.GetFileName(filePath);
        var hash = await CombineHash(fileName, contentSha256Hex, token);
        return (hash, contentSha256Hex);
    }

    public override async Task<string?> PrepareTransferData(string _, CancellationToken token)
    {
        var path = FullPath;
        if (path is null || !File.Exists(path))
        {
            throw new LocalProfileDataUnavailableException(
                $"Transfer data is unavailable for File profile {Hash ?? "<unknown>"}.");
        }

        try
        {
            MarkTransferDataVerified(
                path,
                await ValidateTransferDataHashAsync(path, token));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException &&
                                   ex is not LocalProfileDataUnavailableException &&
                                   !token.IsCancellationRequested)
        {
            throw new LocalProfileDataUnavailableException(
                $"Failed to validate transfer data for File profile {Hash ?? "<unknown>"}.", ex);
        }
    }

    private async Task<string> ValidateTransferDataHashAsync(string path, CancellationToken token)
    {
        var expectedHash = await GetHash(token);
        var hashes = await GetHashesFromFile(path, token);
        if (!string.Equals(hashes.ProfileHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalProfileDataUnavailableException(
                $"File transfer data hash mismatch. Expected: {expectedHash}, Actual: {hashes.ProfileHash}.");
        }

        return hashes.TransferDataHash;
    }

    private async Task<string> ValidatePersistentTransferDataHashAsync(
        string path,
        CancellationToken token)
    {
        if (!HasVerifiedTransferDataHashBinding || !IsValidTransferDataHash(TransferDataHash))
        {
            return await ValidateTransferDataHashAsync(path, token);
        }

        var actualTransferDataHash = await Utility.CalculateFileSHA256(path, token);
        if (!string.Equals(actualTransferDataHash, TransferDataHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalProfileDataUnavailableException(
                $"File transfer data hash mismatch. Expected: {TransferDataHash}, Actual: {actualTransferDataHash}.");
        }

        return actualTransferDataHash;
    }

    public override async Task SetTransferData(
        string path,
        bool verify,
        CancellationToken token)
    {
        EnsureTransferDataExists(path);
        if (!verify)
        {
            await SetUnverifiedTransferData(path, token);
            return;
        }

        SetVerifiedTransferData(
            path,
            await GetHashesFromFile(path, token),
            verifyProfileSemantic: true);
    }

    public override async Task SetTransferData(
        string path,
        string transferDataHash,
        bool verify,
        CancellationToken token)
    {
        EnsureTransferDataExists(path);
        var normalizedTransferDataHash = NormalizeVerifiedTransferDataHash(transferDataHash);
        SetVerifiedTransferData(
            path,
            (
                await CombineHash(Path.GetFileName(path), normalizedTransferDataHash, token),
                normalizedTransferDataHash),
            verifyProfileSemantic: verify);
    }

    private static void EnsureTransferDataExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File does not exist: {path}", path);
        }
    }

    private async Task SetUnverifiedTransferData(string path, CancellationToken token)
    {
        if (!HasVerifiedTransferDataHashBinding)
        {
            SetTransferDataHashBindingVerification(bindingVerified: false);
            SetTransferDataPath(path);
            return;
        }

        var previousTransferDataHash = TransferDataHash;
        var hashes = await GetHashesFromFile(path, token);
        Hash ??= hashes.ProfileHash;
        if (string.Equals(previousTransferDataHash, hashes.TransferDataHash, StringComparison.OrdinalIgnoreCase))
        {
            MarkTransferDataVerified(path, hashes.TransferDataHash);
        }
        else
        {
            SetUnverifiedTransferDataHash(hashes.TransferDataHash);
        }
        SetTransferDataPath(path);
    }

    private void SetVerifiedTransferData(
        string path,
        (string ProfileHash, string TransferDataHash) hashes,
        bool verifyProfileSemantic)
    {
        if (verifyProfileSemantic &&
            Hash is not null &&
            !string.Equals(hashes.ProfileHash, Hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Hash mismatch for the provided file.");
        }

        Hash ??= hashes.ProfileHash;
        MarkTransferDataVerified(path, hashes.TransferDataHash);
        SetTransferDataPath(path);
    }

    private void SetTransferDataPath(string path)
    {
        FullPath = path;
        FileName = Path.GetFileName(path);
    }

    public override async Task SetAndMoveTransferData(
        string persistentDir,
        string path,
        CancellationToken token)
    {
        if (File.Exists(FullPath))
        {
            return;
        }

        await SetTransferData(path, verify: true, token);

        var workingDir = CreateWorkingDir(persistentDir, Type, Hash!);
        var persistentPath = GetPersistentPath(workingDir, path);

        if (Path.IsPathRooted(persistentPath!) is false)
        {
            return;
        }

        var targetPath = Path.Combine(workingDir, FileName);
        File.Move(path, targetPath, true);
        MoveVerifiedTransferData(path, targetPath);
        FullPath = targetPath;
    }

    public override async Task<bool> IsLocalDataValid(bool quick, CancellationToken token)
    {
        if (string.IsNullOrEmpty(FullPath))
            return false;

        if (!File.Exists(FullPath))
            return false;

        if (quick)
            return true;

        if (Hash is null)
        {
            return true;
        }

        try
        {
            var hash = await GetSHA256HashFromFile(FullPath, token);
            return string.Equals(hash, Hash, StringComparison.OrdinalIgnoreCase);
        }
        catch when (token.IsCancellationRequested is false)
        {
            return false;
        }
    }

    public override async Task<string?> NeedsTransferData(string persistentDir, CancellationToken token)
    {
        if (await IsLocalDataValid(false, token))
        {
            return null;
        }

        var workingDir = CreateWorkingDir(persistentDir, Type, await GetHash(token));
        if (FullPath is null)
        {
            return Path.Combine(workingDir, FileName);
        }

        return FullPath;
    }

    public override async Task<ProfilePersistentInfo> Persist(
        string persistentDir,
        CancellationToken token)
    {
        if (FullPath is null)
        {
            throw new Exception("Cannot persist a FileProfile with no data.");
        }

        var workingDir = QueryGetWorkingDir(persistentDir, Type, await GetHash(token));
        if (!IsTransferDataVerified(FullPath))
        {
            MarkTransferDataVerified(
                FullPath,
                await ValidatePersistentTransferDataHashAsync(FullPath, token));
        }
        var path = GetPersistentPath(workingDir, FullPath);
        return new ProfilePersistentInfo
        {
            Type = Type,
            Text = FileName,
            Size = await GetSize(token),
            Hash = await GetHash(token),
            TransferDataFile = path,
            TransferDataHash = TransferDataHash,
            FilePaths = [path],
        };
    }

    public override Task<ProfileLocalInfo> Localize(string localDir, bool quick, CancellationToken token)
    {
        if (FullPath is null)
        {
            throw new Exception("Cannot localize a FileProfile with no data.");
        }

        return Task.FromResult(new ProfileLocalInfo
        {
            Text = FullPath,
            FilePaths = [FullPath],
        });
    }

    public override void CopyTo(Profile target)
    {
        if (target is not FileProfile fileTarget)
            return;

        fileTarget.FullPath = FullPath;
        fileTarget.FileName = FileName;
        fileTarget.Hash = Hash;
        fileTarget.Size = Size;
        CopyTransferDataHashStateTo(fileTarget);
    }
}
