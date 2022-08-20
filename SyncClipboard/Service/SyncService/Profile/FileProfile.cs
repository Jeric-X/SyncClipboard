using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SyncClipboard.Module;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Notification;
using SyncClipboard.Utility.Web;
using static SyncClipboard.Service.ProfileType;
using Button = SyncClipboard.Utility.Notification.Button;
#nullable enable

namespace SyncClipboard.Service
{
    public class FileProfile : Profile
    {
        protected string? fullPath;
        private string statusTip = "";
        private readonly IWebDav? _webDav;
        private const string MD5_FOR_OVERSIZED_FILE = "MD5_FOR_OVERSIZED_FILE";

        public FileProfile(string file)
        {
            FileName = Path.GetFileName(file);
            fullPath = file;
            statusTip = FileName;
        }

        public FileProfile(JsonProfile jsonProfile, IWebDav webDav)
        {
            FileName = jsonProfile.File;
            statusTip = FileName;
            _webDav = webDav;
            SetMd5(jsonProfile.Clipboard);
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
            if (file.Length <= UserConfig.Config.SyncService.MaxFileByte)
            {
                Log.Write("PUSH file " + FileName);
                await webdav.PutFile(remotePath, fullPath, cancelToken);
            }
            else
            {
                Log.Write("file is too large, skipped " + FileName);
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
            if (await GetMD5HashFromFile(localPath, cancelToken) != await GetMd5(cancelToken))
            {
                Log.Write("[PULL] download erro, md5 wrong");
                statusTip = "Downloading erro, md5 wrong";
                throw new Exception("FileProfile download check md5 failed");
            }
            Log.Write("[PULL] download OK " + localPath);
            fullPath = localPath;
            statusTip = FileName;
        }

        public override string ToolTip()
        {
            return statusTip;
        }

        protected override DataObject? CreateDataObject()
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return null;
            }

            var dataObject = new DataObject();

            dataObject.SetFileDropList(new System.Collections.Specialized.StringCollection { fullPath });

            return dataObject;
        }

        protected override async Task<bool> Same(Profile rhs, CancellationToken cancellationToken)
        {
            try
            {
                return await GetMd5(cancellationToken) == await ((FileProfile)rhs).GetMd5(cancellationToken);
            }
            catch
            {
                return false;
            }
        }

        private static async Task<string> GetMD5HashFromFile(string fileName, CancellationToken? cancelToken)
        {
            var fileInfo = new FileInfo(fileName);
            if (fileInfo.Length > UserConfig.Config.SyncService.MaxFileByte)
            {
                return MD5_FOR_OVERSIZED_FILE;
            }
            try
            {
                Log.Write("calc md5 start");
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
                Log.Write($"md5 {md5}");
                return md5;
            }
            catch (System.Exception ex)
            {
                Log.Write("GetMD5HashFromFile() fail " + ex.Message);
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

        protected override void AfterSetLocal()
        {
            var path = fullPath ?? GetTempLocalFilePath();
            Toast.SendText(
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
    }
}
