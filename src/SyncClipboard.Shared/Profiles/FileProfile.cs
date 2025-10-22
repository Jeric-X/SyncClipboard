using SyncClipboard.Shared;
using SyncClipboard.Shared.Models;
using System.Security.Cryptography;
using System.Text;

namespace SyncClipboard.Shared.Profiles;

public class FileProfile : Profile
{
    protected const int MAX_FILE_SIZE = int.MaxValue;

    public virtual string FileName { get; set; } = "";
    public override ProfileType Type => ProfileType.File;
    public virtual string? FullPath { get; set; }
    protected string? _hash;
    protected long? _size;

    protected const string MD5_FOR_OVERSIZED_FILE = "MD5_FOR_OVERSIZED_FILE";

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

    public virtual ValueTask<long> GetSize(CancellationToken token)
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

    public virtual async ValueTask<string> GetHash(CancellationToken token)
    {
        if (_hash is null)
        {
            if (FullPath is null)
            {
                return string.Empty;
            }

            _hash = await GetMD5HashFromFile(FullPath, token); ;
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
            var md5ThisTask = GetHash(token);
            var md5OtherTask = ((FileProfile)rhs).GetHash(token);
            var md5This = await md5ThisTask;
            var md5Other = await md5OtherTask;
            if (string.IsNullOrEmpty(md5This) || string.IsNullOrEmpty(md5Other))
            {
                return false;
            }
            return md5This == md5Other;
        }
        catch
        {
            return false;
        }
    }

    protected async static Task<string> GetMD5HashFromFile(string fileName, CancellationToken? cancelToken)
    {
        var fileInfo = new FileInfo(fileName);
        if (fileInfo.Length > MAX_FILE_SIZE)
        {
            return MD5_FOR_OVERSIZED_FILE;
        }

        var file = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var md5Oper = MD5.Create();
        var retVal = await md5Oper.ComputeHashAsync(file, cancelToken ?? CancellationToken.None);
        file.Close();

        var sb = new StringBuilder();
        for (int i = 0; i < retVal.Length; i++)
        {
            sb.Append(retVal[i].ToString("x2"));
        }
        string md5 = sb.ToString();
        return md5;
    }

    public virtual async Task CheckDownloadedData(CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(FullPath);
        ArgumentNullException.ThrowIfNull(_hash);
        if (!File.Exists(FullPath))
        {
            throw new FileNotFoundException($"File does not exist: {FullPath}", FullPath);
        }

        var hash = await GetMD5HashFromFile(FullPath, token);
        if (hash != _hash)
        {
            throw new InvalidDataException(FullPath);
        }
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
            var hash = await GetMD5HashFromFile(FullPath, token);
            return hash == _hash;
        }
        catch
        {
            return false;
        }
    }
}
