using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Notification;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Button = SyncClipboard.Core.Utilities.Notification.Button;
#nullable enable

namespace SyncClipboard.Service
{
    public class FileProfile : Profile
    {
        public override string Text { get => _fileMd5Hash; set => base.Text = value; }
        public override ProfileType Type => ProfileType.File;

        protected override IClipboardSetter<Profile> ClipboardSetter { get; set; }

        protected string? FullPath { get; set; }

        private string _fileMd5Hash = "";
        private string _statusTip = "";
        private readonly IWebDav? _webDav;
        private readonly ILogger _logger;
        private readonly UserConfig _userConfig;
        private const string MD5_FOR_OVERSIZED_FILE = "MD5_FOR_OVERSIZED_FILE";

        public FileProfile(string file, IServiceProvider serviceProvider) : this(serviceProvider)
        {
            FileName = Path.GetFileName(file);
            FullPath = file;
            _statusTip = FileName;
        }

        public FileProfile(ClipboardProfileDTO profileDTO, IServiceProvider serviceProvider) : this(serviceProvider)
        {
            FileName = profileDTO.File;
            _statusTip = FileName;
            SetFileHash(profileDTO.Clipboard);
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

        private void SetFileHash(string md5)
        {
            _fileMd5Hash = md5;
        }

        private async Task<string> GetFileHash(CancellationToken cancelToken)
        {
            await CalcFileHash(cancelToken);
            return _fileMd5Hash;
        }

        private async Task CalcFileHash(CancellationToken cancelToken)
        {
            if (string.IsNullOrEmpty(_fileMd5Hash) && !string.IsNullOrEmpty(FullPath))
            {
                SetFileHash(await GetMD5HashFromFile(FullPath, cancelToken));
            }
        }

        public override async Task UploadProfile(IWebDav webdav, CancellationToken cancelToken)
        {
            string remotePath = $"{SyncService.REMOTE_FILE_FOLDER}/{FileName}";

            ArgumentNullException.ThrowIfNull(FullPath);
            var file = new FileInfo(FullPath);
            if (file.Length <= _userConfig.Config.SyncService.MaxFileByte)
            {
                _logger.Write("PUSH file " + FileName);
                if (!await webdav.Exist(SyncService.REMOTE_FILE_FOLDER))
                {
                    await webdav.CreateDirectory(SyncService.REMOTE_FILE_FOLDER);
                }
                await webdav.PutFile(remotePath, FullPath, cancelToken);
            }
            else
            {
                _logger.Write("file is too large, skipped " + FileName);
            }

            await CalcFileHash(cancelToken);
            await webdav.PutText(SyncService.REMOTE_RECORD_FILE, this.ToJsonString(), cancelToken);
        }

        public override async Task BeforeSetLocal(CancellationToken cancelToken,
            IProgress<HttpDownloadProgress>? progress = null)
        {
            if (!string.IsNullOrEmpty(FullPath))
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
            FullPath = localPath;
            _statusTip = FileName;
        }

        private async Task CheckHash(string localPath, CancellationToken cancelToken)
        {
            var downloadFIleMd5 = GetMD5HashFromFile(localPath, cancelToken);
            var existedMd5 = GetFileHash(cancelToken);
            if (string.IsNullOrEmpty(await existedMd5))
            {
                SetFileHash(await downloadFIleMd5);
                await (_webDav?.PutText(SyncService.REMOTE_RECORD_FILE, ToJsonString(), cancelToken) ?? Task.CompletedTask);
                return;
            }

            if (await downloadFIleMd5 != await existedMd5)
            {
                _logger.Write("[PULL] download erro, md5 wrong");
                _statusTip = "Downloading erro, md5 wrong";
                throw new Exception("FileProfile download check md5 failed");
            }
        }

        public override string ToolTip()
        {
            return _statusTip;
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
            if (fileInfo.Length > _userConfig.Config.SyncService.MaxFileByte)
            {
                return MD5_FOR_OVERSIZED_FILE;
            }
            try
            {
                _logger.Write("calc md5 start");
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
            var path = FullPath ?? GetTempLocalFilePath();
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
            var path = FullPath ?? GetTempLocalFilePath();
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
            return await GetFileHash(cancelToken) == MD5_FOR_OVERSIZED_FILE;
        }

        protected override MetaInfomation CreateMetaInformation()
        {
            ArgumentNullException.ThrowIfNull(FullPath);
            return new MetaInfomation() { Files = new string[] { FullPath } };
        }
    }
}