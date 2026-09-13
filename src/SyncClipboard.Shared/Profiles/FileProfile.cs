using SyncClipboard.Shared.Models;
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
        TransferDataHash = entity.TransferDataHash;
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
        TransferDataHash = dto.TransferDataHash;
    }

    protected override async Task ComputeHash(CancellationToken token)
    {
        if (FullPath is null || !File.Exists(FullPath))
        {
            return;
        }

        var hashes = await GetHashesFromFile(FullPath, token);
        Hash = hashes.ProfileHash;
        TransferDataHash = hashes.TransferDataHash;
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
            TransferDataHash = TransferDataHash,
            Size = await GetSize(token)
        };
    }

    protected async static Task<string> CombineHash(string fileName, string contentHash, CancellationToken token)
    {
        var combinedString = $"{fileName}|{contentHash.ToUpperInvariant()}";
        var hash = await Utility.CalculateSHA256(combinedString, token);
        return hash;
    }

    private protected async static Task<(string ProfileHash, string TransferDataHash)> GetHashesFromFile(
        string filePath, CancellationToken token)
    {
        var contentSha256Hex = await Utility.CalculateFileSHA256(filePath, token);
        var fileName = Path.GetFileName(filePath);
        var hash = await CombineHash(fileName, contentSha256Hex, token);
        return (hash, contentSha256Hex);
    }

    public override async Task<FileHashInfo?> PrepareTransferData(string _, CancellationToken token)
    {
        var path = FullPath;
        if (path is null || !File.Exists(path))
        {
            throw new LocalProfileDataUnavailableException(
                $"Transfer data is unavailable for File profile {Hash ?? "<unknown>"}.");
        }

        try
        {
            var hashes = await GetHashesFromFile(path, token);
            if (Hash is not null && !Utility.SHA256Same(hashes.ProfileHash, Hash))
            {
                throw new LocalProfileDataUnavailableException(
                    $"File transfer data hash mismatch. Expected: {Hash}, Actual: {hashes.ProfileHash}.");
            }

            Hash ??= hashes.ProfileHash;
            TransferDataHash = hashes.TransferDataHash;
            return new FileHashInfo(path, hashes.TransferDataHash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException &&
                                   ex is not LocalProfileDataUnavailableException &&
                                   !token.IsCancellationRequested)
        {
            throw new LocalProfileDataUnavailableException(
                $"Failed to validate transfer data for File profile {Hash ?? "<unknown>"}.", ex);
        }
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
        var fileName = Path.GetFileName(path);
        if (transferDataHash is not null)
        {
            var profileHash = await CombineHash(fileName, transferDataHash, token);
            if (verify && Hash is not null && !Utility.SHA256Same(profileHash, Hash))
            {
                throw new InvalidDataException("Hash mismatch for the provided file.");
            }
            Hash ??= profileHash;
        }

        TransferDataHash = transferDataHash;
        FullPath = path;
        FileName = fileName;
    }

    private static void EnsureTransferDataExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File does not exist: {path}", path);
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

        var targetPath = Path.Combine(workingDir, FileName);
        File.Move(path, targetPath, true);
        FullPath = targetPath;
    }

    public override async Task<bool> IsLocalDataValid(bool quick, CancellationToken token)
    {
        var path = FullPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        if (quick)
            return true;

        try
        {
            var hashes = await GetHashesFromFile(path, token);
            if (Hash is not null && !Utility.SHA256Same(hashes.ProfileHash, Hash))
            {
                return false;
            }

            Hash ??= hashes.ProfileHash;
            TransferDataHash = hashes.TransferDataHash;
            return true;
        }
        catch when (token.IsCancellationRequested is false)
        {
            return false;
        }
    }

    public override Task<bool> IsDataComplete(bool quick, CancellationToken token)
    {
        return IsLocalDataValid(quick, token);
    }

    public override async Task<bool> TryLocalize(
        string localDir, bool clearInvalidLocalPaths = false, CancellationToken token = default)
    {
        var valid = await IsLocalDataValid(false, token);
        if (!valid && clearInvalidLocalPaths && !token.IsCancellationRequested)
        {
            FullPath = null;
        }
        return valid;
    }

    public override string GetTransferDataSavePath(string persistentDir)
    {
        var workingDir = QueryGetWorkingDir(persistentDir, Type, Hash ?? string.Empty);
        return Path.Combine(workingDir, FileName);
    }

    public override async Task<ProfilePersistentInfo> Persist(string persistentDir, CancellationToken token)
    {
        if (FullPath is null)
        {
            throw new Exception("Cannot persist a FileProfile with no data.");
        }

        var workingDir = QueryGetWorkingDir(persistentDir, Type, await GetHash(token));
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

    public override Task<ProfileLocalInfo> Localize(string localDir, CancellationToken token)
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
        fileTarget.TransferDataHash = TransferDataHash;
    }
}
