using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Notification;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static SyncClipboard.Service.ProfileType;
using Button = SyncClipboard.Core.Utilities.Notification.Button;
#nullable enable

namespace SyncClipboard.Service
{
    public class FileProfile : Profile
    {
        protected string? fullPath;
        private string statusTip = "";
        private readonly IWebDav? _webDav;
        private const string MD5_FOR_OVERSIZED_FILE = "MD5_FOR_OVERSIZED_FILE";
        private readonly ILogger _logger;
        private readonly UserConfig _userConfig;
        protected override IClipboardSetter<Profile> ClipboardSetter { get; set; }

        public override Core.Clipboard.ProfileType Type => Core.Clipboard.ProfileType.File;

        public FileProfile(string file, IServiceProvider serviceProvider) : this(serviceProvider)
        {
            FileName = Path.GetFileName(file);
            fullPath = file;
            statusTip = FileName;
        }

        public FileProfile(JsonProfile jsonProfile, IServiceProvider serviceProvider) : this(serviceProvider)
        {
            FileName = jsonProfile.File;
            statusTip = FileName;
            SetMd5(jsonProfile.Clipboard);
        }

        private FileProfile(IServiceProvider serviceProvider)
        {
            ClipboardSetter = serviceProvider.GetRequiredService<IClipboardSetter<FileProfile>>();
            _webDav = serviceProvider.GetRequiredService<IWebDav>();
            _logger = serviceProvider.GetRequiredService<ILogger>();
            _userConfig = serviceProvider.GetRequiredService<UserConfig>();
        }

        protected string GetTempLocalFilePath()
        {
            return Path.Combine(SyncService.LOCAL_FILE_FOLDER, FileName);
        }

        public override ClipboardType GetProfileType()
        {
            return ClipboardType.File;
        }

        private void SetMd5(string md5)
        {
            Text = md5;
        }

        private async Task<string> GetMd5(CancellationToken cancelToken)
        {
            if (string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(fullPath))
            {
                SetMd5(await GetMD5HashFromFile(fullPath, cancelToken));
            }
            return Text;
        }

        public override async Task UploadProfileAsync(IWebDav webdav, CancellationToken cancelToken)
        {
            string remotePath = $"{SyncService.REMOTE_FILE_FOLDER}/{FileName}";

            ArgumentNullException.ThrowIfNull(fullPath);
            var file = new FileInfo(fullPath);
            if (file.Length <= _userConfig.Config.SyncService.MaxFileByte)
            {
                _logger.Write("PUSH file " + FileName);
                if (!await webdav.Exist(SyncService.REMOTE_FILE_FOLDER))
                {
                    await webdav.CreateDirectory(SyncService.REMOTE_FILE_FOLDER);
                }
                await webdav.PutFile(remotePath, fullPath, cancelToken);
            }
            else
            {
                _logger.Write("file is too large, skipped " + FileName);
            }

            SetMd5(await GetMd5(cancelToken));
            await webdav.PutText(SyncService.REMOTE_RECORD_FILE, this.ToJsonString(), cancelToken);
        }

        public override async Task BeforeSetLocal(CancellationToken cancelToken,
            IProgress<HttpDownloadProgress>? progress = null)
        {
            if (!string.IsNullOrEmpty(fullPath))
            {
                return;
            }

            if (_webDav is null)
            {
                return;
            }

            string remotePath = $"{SyncService.REMOTE_FILE_FOLDER}/{FileName}";
            string localPath = GetTempLocalFilePath();

            await _webDav.GetFile(remotePath, localPath, progress, cancelToken);
            await CheckHash(localPath, cancelToken);

            _logger.Write("[PULL] download OK " + localPath);
            fullPath = localPath;
            statusTip = FileName;
        }

        private async Task CheckHash(string localPath, CancellationToken cancelToken)
        {
            var downloadFIleMd5 = GetMD5HashFromFile(localPath, cancelToken);
            var existedMd5 = GetMd5(cancelToken);
            if (string.IsNullOrEmpty(await existedMd5))
            {
                SetMd5(await downloadFIleMd5);
                await (_webDav?.PutText(SyncService.REMOTE_RECORD_FILE, ToJsonString(), cancelToken) ?? Task.CompletedTask);
                return;
            }

            if (await downloadFIleMd5 != await existedMd5)
            {
                _logger.Write("[PULL] download erro, md5 wrong");
                statusTip = "Downloading erro, md5 wrong";
                throw new Exception("FileProfile download check md5 failed");
            }
        }

        public override string ToolTip()
        {
            return statusTip;
        }

        protected override async Task<bool> Same(Profile rhs, CancellationToken cancellationToken)
        {
            try
            {
                var md5This = await GetMd5(cancellationToken);
                var md5Other = await ((FileProfile)rhs).GetMd5(cancellationToken);
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
            if (fileInfo.Length > _userConfig.Config.SyncService.MaxFileByte)
            {
                return MD5_FOR_OVERSIZED_FILE;
            }
            try
            {
                _logger.Write("calc md5 start");
                var file = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var md5Oper = System.Security.Cryptography.MD5.Create();
                var retVal = await md5Oper.ComputeHashAsync(file, cancelToken ?? CancellationToken.None);
                file.Close();

                var sb = new StringBuilder();
                for (int i = 0; i < retVal.Length; i++)
                {
                    sb.Append(retVal[i].ToString("x2"));
                }
                string md5 = sb.ToString();
                _logger.Write($"md5 {md5}");
                return md5;
            }
            catch (System.Exception ex)
            {
                _logger.Write("GetMD5HashFromFile() fail " + ex.Message);
                throw;
            }
        }

        public Action<string> OpenInExplorer()
        {
            var path = fullPath ?? GetTempLocalFilePath();
            return (_) =>
            {
                var open = new System.Diagnostics.Process();
                open.StartInfo.FileName = "explorer";
                open.StartInfo.Arguments = "/e,/select," + path;
                open.Start();
            };
        }

        protected override void SetNotification(NotificationManager notification)
        {
            var path = fullPath ?? GetTempLocalFilePath();
            notification.SendText(
                "文件同步成功",
                FileName,
                DefaultButton(),
                new Button("打开文件夹", new Callbacker(Guid.NewGuid().ToString(), OpenInExplorer())),
                new Button("打开", new Callbacker(Guid.NewGuid().ToString(), (_) => Sys.OpenWithDefaultApp(path)))
            );
        }

        public async Task<bool> Oversized(CancellationToken cancelToken)
        {
            return await GetMd5(cancelToken) == MD5_FOR_OVERSIZED_FILE;
        }

        protected override MetaInfomation CreateMetaInformation()
        {
            ArgumentNullException.ThrowIfNull(fullPath);
            return new MetaInfomation() { Files = new string[] { fullPath } };
        }
    }
}