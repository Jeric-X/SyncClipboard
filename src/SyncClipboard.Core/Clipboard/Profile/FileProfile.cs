using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
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
    private string? _statusTip;
    private string StatusTip => string.IsNullOrEmpty(_statusTip) ? FileName : _statusTip;

    private static readonly string RemoteFileFolder = Env.RemoteFileFolder;

    protected FileProfile(string fullPath, string hash) : this(fullPath, Path.GetFileName(fullPath), hash)
    {
    }

    public FileProfile(ClipboardProfileDTO profileDTO) : this(null, profileDTO.File, profileDTO.Clipboard)
    {
    }

    private FileProfile(string? fullPath, string fileName, string hash)
    {
        Hash = hash;
        FullPath = fullPath;
        FileName = fileName;
    }

    public static async Task<FileProfile> Create(string fullPath, CancellationToken token)
    {
        var hash = await GetMD5HashFromFile(fullPath, token);
        return new FileProfile(fullPath, hash);
    }

    protected string GetTempLocalFilePath()
    {
        return Path.Combine(LocalTemplateFolder, FileName);
    }

    public override async Task UploadProfile(IWebDav webdav, CancellationToken cancelToken)
    {
        string remotePath = $"{RemoteFileFolder}/{FileName}";

        ArgumentNullException.ThrowIfNull(FullPath);
        if (!Oversized())
        {
            Logger.Write("PUSH file " + FileName);
            if (!await webdav.DirectoryExist(RemoteFileFolder))
            {
                await webdav.CreateDirectory(RemoteFileFolder);
            }
            await webdav.PutFile(remotePath, FullPath, cancelToken);
        }
        else
        {
            Logger.Write("file is too large, skipped " + FileName);
        }

        await webdav.PutJson(RemoteProfilePath, ToDto(), cancelToken);
    }

    public override async Task BeforeSetLocal(CancellationToken cancelToken,
        IProgress<HttpDownloadProgress>? progress = null)
    {
        if (!string.IsNullOrEmpty(FullPath))
        {
            return;
        }

        if (WebDav is null)
        {
            return;
        }

        string remotePath = $"{RemoteFileFolder}/{FileName}";
        string localPath = GetTempLocalFilePath();

        await WebDav.GetFile(remotePath, localPath, progress, cancelToken);
        await CheckHash(localPath, cancelToken);

        Logger.Write("[PULL] download OK " + localPath);
        FullPath = localPath;
        _statusTip = FileName;
    }

    protected virtual async Task CheckHash(string localPath, CancellationToken cancelToken)
    {
        var downloadedMd5 = await GetMD5HashFromFile(localPath, cancelToken);
        var existedMd5 = Hash;
        if (string.IsNullOrEmpty(existedMd5))
        {
            Hash = downloadedMd5;
            await WebDav!.PutJson(RemoteProfilePath, ToDto(), cancelToken);
            return;
        }

        if (downloadedMd5 != MD5_FOR_OVERSIZED_FILE
            && existedMd5 != MD5_FOR_OVERSIZED_FILE
            && downloadedMd5 != existedMd5)
        {
            Logger.Write("[PULL] download erro, md5 wrong");
            _statusTip = "Downloading erro, md5 wrong";
            throw new Exception("FileProfile download check md5 failed");
        }
    }

    public override string ToolTip()
    {
        return StatusTip;
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

    protected async static Task<string> GetMD5HashFromFile(string fileName, CancellationToken? cancelToken)
    {
        var fileInfo = new FileInfo(fileName);
        if (fileInfo.Length > Config.GetConfig<SyncConfig>().MaxFileByte)
        {
            return MD5_FOR_OVERSIZED_FILE;
        }
        try
        {
            Logger.Write("calc md5 start");
            var file = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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
        notification.SendText(
            I18n.Strings.ClipboardFileUpdated,
            FileName,
            DefaultButton(),
#if WINDOWS
            new Button(I18n.Strings.OpenFolder, () => Sys.OpenFileInExplorer(FullPath)),
#endif
            new Button(I18n.Strings.Open, () => Sys.OpenWithDefaultApp(FullPath))
        );
    }

    private bool Oversized()
    {
        return Hash == MD5_FOR_OVERSIZED_FILE;
    }

    public override bool IsAvailableFromRemote() => !Oversized();

    public override async Task EnsureAvailable(CancellationToken token)
    {
        if (!await WebDav.Exist($"{RemoteFileFolder}/{FileName}", token))
        {
            throw new Exception("Remote file is lost.");
        }
    }

    protected override ClipboardMetaInfomation CreateMetaInformation()
    {
        ArgumentNullException.ThrowIfNull(FullPath);
        return new ClipboardMetaInfomation() { Files = new string[] { FullPath }, Text = FileName };
    }
}