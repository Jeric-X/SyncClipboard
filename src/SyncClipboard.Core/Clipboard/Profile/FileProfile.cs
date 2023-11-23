using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
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
    public override string Text { get => _fileMd5Hash; set => base.Text = value; }
    public override ProfileType Type => ProfileType.File;

    protected override IClipboardSetter<Profile> ClipboardSetter { get; set; }
    protected override IServiceProvider ServiceProvider { get; set; }

    public virtual string? FullPath { get; set; }

    private const string MD5_FOR_OVERSIZED_FILE = "MD5_FOR_OVERSIZED_FILE";
    private readonly uint _maxFileByte;
    private string _fileMd5Hash = "";
    private string? _statusTip;
    private string StatusTip => string.IsNullOrEmpty(_statusTip) ? FileName : _statusTip;

    private readonly IWebDav? WebDav;
    private readonly ILogger Logger;
    private readonly string RemoteFileFolder;

    public FileProfile(string file, IServiceProvider serviceProvider) : this(serviceProvider)
    {
        FileName = Path.GetFileName(file);
        FullPath = file;
    }

    public FileProfile(ClipboardProfileDTO profileDTO, IServiceProvider serviceProvider) : this(serviceProvider)
    {
        FileName = profileDTO.File;
        SetFileHash(profileDTO.Clipboard);
    }

    private FileProfile(IServiceProvider serviceProvider)
    {
        ClipboardSetter = serviceProvider.GetRequiredService<IClipboardSetter<FileProfile>>();
        WebDav = serviceProvider.GetRequiredService<IWebDav>();
        Logger = serviceProvider.GetRequiredService<ILogger>();
        ServiceProvider = serviceProvider;
        RemoteFileFolder = Env.RemoteFileFolder;

        var configManager = serviceProvider.GetRequiredService<ConfigManager>();
        var syncConfig = configManager.GetConfig<SyncConfig>();
        _maxFileByte = syncConfig.MaxFileByte;
    }

    protected string GetTempLocalFilePath()
    {
        return Path.Combine(LocalTemplateFolder, FileName);
    }

    private void SetFileHash(string md5)
    {
        _fileMd5Hash = md5;
    }

    private async Task<string> GetFileHash(CancellationToken cancelToken)
    {
        await CalcFileHash(cancelToken);
        return _fileMd5Hash;
    }

    public async Task CalcFileHash(CancellationToken cancelToken)
    {
        if (string.IsNullOrEmpty(_fileMd5Hash) && !string.IsNullOrEmpty(FullPath))
        {
            SetFileHash(await GetMD5HashFromFile(FullPath, cancelToken));
        }
    }

    public override async Task UploadProfile(IWebDav webdav, CancellationToken cancelToken)
    {
        string remotePath = $"{RemoteFileFolder}/{FileName}";

        ArgumentNullException.ThrowIfNull(FullPath);
        var file = new FileInfo(FullPath);
        if (file.Length <= _maxFileByte)
        {
            Logger.Write("PUSH file " + FileName);
            if (!await webdav.Exist(RemoteFileFolder))
            {
                await webdav.CreateDirectory(RemoteFileFolder);
            }
            await webdav.PutFile(remotePath, FullPath, cancelToken);
        }
        else
        {
            Logger.Write("file is too large, skipped " + FileName);
        }

        await CalcFileHash(cancelToken);
        await webdav.PutText(RemoteProfilePath, this.ToJsonString(), cancelToken);
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

    private async Task CheckHash(string localPath, CancellationToken cancelToken)
    {
        var downloadFIleMd5 = await GetMD5HashFromFile(localPath, cancelToken);
        var existedMd5 = await GetFileHash(cancelToken);
        if (string.IsNullOrEmpty(existedMd5))
        {
            SetFileHash(downloadFIleMd5);
            await (WebDav?.PutText(RemoteProfilePath, ToJsonString(), cancelToken) ?? Task.CompletedTask);
            return;
        }

        if (downloadFIleMd5 != MD5_FOR_OVERSIZED_FILE
            && existedMd5 != MD5_FOR_OVERSIZED_FILE
            && downloadFIleMd5 != existedMd5)
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

    protected override async Task<bool> Same(Profile rhs, CancellationToken cancellationToken)
    {
        try
        {
            var md5This = await GetFileHash(cancellationToken);
            var md5Other = await ((FileProfile)rhs).GetFileHash(cancellationToken);
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

    private async Task<string> GetMD5HashFromFile(string fileName, CancellationToken? cancelToken)
    {
        var fileInfo = new FileInfo(fileName);
        if (fileInfo.Length > _maxFileByte)
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

    public Action<string> OpenInExplorer()
    {
        var path = FullPath ?? GetTempLocalFilePath();
        return (_) =>
        {
            var open = new System.Diagnostics.Process();
            open.StartInfo.FileName = "explorer";
            open.StartInfo.Arguments = "/e,/select," + path;
            open.Start();
        };
    }

    protected override void SetNotification(INotification notification)
    {
        var path = FullPath ?? GetTempLocalFilePath();
        notification.SendText(
            I18n.Strings.ClipboardFileUpdated,
            FileName,
            DefaultButton(),
            new Button(I18n.Strings.OpenFolder, new Callbacker(Guid.NewGuid().ToString(), OpenInExplorer())),
            new Button(I18n.Strings.Open, new Callbacker(Guid.NewGuid().ToString(), (_) => Sys.OpenWithDefaultApp(path)))
        );
    }

    public async Task<bool> Oversized(CancellationToken cancelToken)
    {
        return await GetFileHash(cancelToken) == MD5_FOR_OVERSIZED_FILE;
    }

    protected override ClipboardMetaInfomation CreateMetaInformation()
    {
        ArgumentNullException.ThrowIfNull(FullPath);
        return new ClipboardMetaInfomation() { Files = new string[] { FullPath }, Text = FileName };
    }
}