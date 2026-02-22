using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Profiles;

public class FileProfile : Profile
{
    protected const long MAX_FILE_SIZE = int.MaxValue;

    public virtual string FileName { get; set; } = "";
    public override string DisplayText => FileName;

    public override string ShortDisplayText => FileName;
    public override ProfileType Type => ProfileType.File;
    public virtual string? FullPath { get; set; }
    public override bool HasTransferData => true;

    protected const string HASH_FOR_OVERSIZED_FILE = "HASH_FOR_OVERSIZED_FILE";

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
    }

    protected override async Task ComputeHash(CancellationToken token)
    {
        if (FullPath is null || !File.Exists(FullPath))
        {
            return;
        }
        Hash = await GetSHA256HashFromFile(FullPath, token);
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
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MAX_FILE_SIZE)
        {
            return HASH_FOR_OVERSIZED_FILE;
        }

        var contentSha256Hex = await Utility.CalculateFileSHA256(filePath, cancelToken.Value);
        var fileName = Path.GetFileName(filePath);
        var hash = await CombineHash(fileName, contentSha256Hex, cancelToken.Value);
        return hash;
    }

    public override Task<string?> PrepareTransferData(string _, CancellationToken token)
    {
        if (FullPath is not null && File.Exists(FullPath))
        {
            return Task.FromResult<string?>(FullPath);
        }

        throw new FileNotFoundException("File not found for transfer", FullPath);
    }

    public override async Task SetTransferData(string path, bool verify, CancellationToken token)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File does not exist: {path}", path);
        }

        if (!verify)
        {
            FullPath = path;
            FileName = Path.GetFileName(path);
            return;
        }

        var hash = await GetSHA256HashFromFile(path, token);
        if (Hash is not null && string.Equals(hash, Hash, StringComparison.OrdinalIgnoreCase) is false)
        {
            throw new InvalidDataException("Hash mismatch for the provided file.");
        }
        Hash = hash;
        FullPath = path;
        FileName = Path.GetFileName(path);
    }

    public override async Task SetAndMoveTransferData(string persistentDir, string path, CancellationToken token)
    {
        if (File.Exists(FullPath))
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

        var targetPath = Path.Combine(workingDir, FileName);
        File.Move(path, targetPath, true);
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

        if (FullPath is null)
        {
            return Path.Combine(CreateWorkingDir(persistentDir, Type, await GetHash(token)), FileName);
        }

        return FullPath;
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
    }
}
