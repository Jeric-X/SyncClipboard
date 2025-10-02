using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Image;
using System.Security.Cryptography;
using System.Text;

namespace SyncClipboard.Core.Clipboard;

public class FileProfile : Profile
{
    public override string Text { get => Hash; set => Hash = value; }
    public override ProfileType Type => ProfileType.File;
    public virtual string? FullPath { get; set; }
    public virtual string Hash { get; set; }

    protected override IClipboardSetter<Profile> ClipboardSetter
        => ServiceProvider.GetRequiredService<IClipboardSetter<FileProfile>>();

    protected const string MD5_FOR_OVERSIZED_FILE = "MD5_FOR_OVERSIZED_FILE";

    protected FileProfile(string fullPath, string hash, bool contentControl = true)
        : this(fullPath, Path.GetFileName(fullPath), hash, contentControl)
    {
    }

    public FileProfile(ClipboardProfileDTO profileDTO) : this(null, profileDTO.File, profileDTO.Clipboard)
    {
    }

    private FileProfile(string? fullPath, string fileName, string hash, bool contentControl = true)
    {
        Hash = hash;
        FullPath = fullPath;
        FileName = fileName;
        ContentControl = contentControl;
    }

    public static async Task<FileProfile> Create(string fullPath, bool contentControl, CancellationToken token)
    {
        var hash = await GetMD5HashFromFile(fullPath, contentControl, token);
        if (ImageHelper.FileIsImage(fullPath))
        {
            return await ImageProfile.Create(fullPath, contentControl, token);
        }

        return new FileProfile(fullPath, hash, contentControl);
    }

    public static Task<FileProfile> Create(string fullPath, CancellationToken token)
    {
        return Create(fullPath, true, token);
    }

    protected virtual string GetTempLocalFilePath()
    {
        var historyConfig = Config.GetConfig<HistoryConfig>();
        if (historyConfig.EnableHistory)
        {
            var historyFolder = Path.Combine(Env.HistoryFileFolder, Hash);
            Directory.CreateDirectory(historyFolder);
            return Path.Combine(historyFolder, FileName);
        }
        else
        {
            return Path.Combine(LocalTemplateFolder, FileName);
        }
    }

    public override bool HasDataFile => true;

    public override string GetLocalDataPath()
    {
        if (string.IsNullOrEmpty(FullPath))
        {
            FullPath = GetTempLocalFilePath();
        }
        return FullPath;
    }

    public override string ShowcaseText()
    {
        return FileName;
    }

    protected override bool Same(Profile rhs)
    {
        try
        {
            var md5This = Hash;
            var md5Other = ((FileProfile)rhs).Hash;
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

    protected async static Task<string> GetMD5HashFromFile(string fileName, bool checkSize, CancellationToken? cancelToken)
    {
        var fileInfo = new FileInfo(fileName);
        if (checkSize && fileInfo.Length > Config.GetConfig<SyncConfig>().MaxFileByte)
        {
            return MD5_FOR_OVERSIZED_FILE;
        }
        try
        {
            Logger.Write("calc md5 start");
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
            Logger.Write($"md5 {md5}");
            return md5;
        }
        catch (System.Exception ex)
        {
            Logger.Write("GetMD5HashFromFile() fail " + ex.Message);
            throw;
        }
    }

    protected override void SetNotification(INotification notification)
    {
        ArgumentNullException.ThrowIfNull(FullPath);
        notification.Title = I18n.Strings.ClipboardFileUpdated;
        notification.Message = FileName;
        notification.Buttons = [
            DefaultButton(),
            new ActionButton(I18n.Strings.OpenFolder, () => Sys.ShowPathInFileManager(FullPath)),
            new ActionButton(I18n.Strings.Open, () => Sys.OpenWithDefaultApp(FullPath))
        ];
        notification.Show();
    }

    protected bool Oversized()
    {
        return Hash == MD5_FOR_OVERSIZED_FILE;
    }

    public override bool IsAvailableFromRemote() => !Oversized();

    public override bool IsAvailableAfterFilter() => IsFileAvailableAfterFilter(FullPath!)
        && !Oversized() && Config.GetConfig<SyncConfig>().EnableUploadSingleFile;

    protected static bool IsFileAvailableAfterFilter(string fileName)
    {
        var filterConfig = Config.GetConfig<FileFilterConfig>();
        if (filterConfig.FileFilterMode == "BlackList")
        {
            var str = filterConfig.BlackList.Find(str => fileName.EndsWith(str, StringComparison.OrdinalIgnoreCase));
            if (str is not null)
            {
                return false;
            }
        }
        else if (filterConfig.FileFilterMode == "WhiteList")
        {
            var str = filterConfig.WhiteList.Find(str => fileName.EndsWith(str, StringComparison.OrdinalIgnoreCase));
            if (str is null)
            {
                return false;
            }
        }
        return true;
    }

    public override async Task CheckDownloadedData(CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(FullPath);
        if (!File.Exists(FullPath))
        {
            throw new FileNotFoundException($"File does not exist: {FullPath}", FullPath);
        }

        var hash = await GetMD5HashFromFile(FullPath, false, token);
        if (hash != Hash)
        {
            throw new InvalidDataException(FullPath);
        }
    }

    protected override ClipboardMetaInfomation CreateMetaInformation()
    {
        ArgumentNullException.ThrowIfNull(FullPath);
        return new ClipboardMetaInfomation() { Files = [FullPath], Text = FileName };
    }

    public override HistoryRecord CreateHistoryRecord()
    {
        return new HistoryRecord
        {
            Type = ProfileType.File,
            Text = FullPath ?? FileName,
            FilePath = [FullPath ?? string.Empty],
            Hash = Hash
        };
    }
}