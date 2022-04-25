using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SyncClipboard.Module;
using SyncClipboard.Utility;
using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    public class FileProfile : Profile
    {
        protected string fullPath;
        private static readonly long maxFileSize = UserConfig.Config.SyncService.MaxFileByte;
        private string statusTip = "";
        private readonly IWebDav _webDav;
        private const string MD5_FOR_OVERSIZED_FILE = "MD5_FOR_OVERSIZED_FILE";

        public FileProfile(string file)
        {
            FileName = System.IO.Path.GetFileName(file);
            fullPath = file;
        }

        public FileProfile(JsonProfile jsonProfile, IWebDav webDav)
        {
            FileName = jsonProfile.File;
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

        private string GetMd5()
        {
            if (string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(fullPath))
            {
                SetMd5(GetMD5HashFromFile(fullPath));
            }
            return Text;
        }

        public override async Task UploadProfileAsync(IWebDav webdav)
        {
            string remotePath = $"{SyncService.REMOTE_FILE_FOLDER}/{FileName}";

            var file = new FileInfo(fullPath);
            if (file.Length <= maxFileSize)
            {
                Log.Write("PUSH file " + FileName);
                await webdav.PutFileAsync(remotePath, fullPath, 0, 0);
            }
            else
            {
                Log.Write("file is too large, skipped " + FileName);
            }

            SetMd5(GetMD5HashFromFile(fullPath));
            await webdav.PutTextAsync(SyncService.REMOTE_RECORD_FILE, this.ToJsonString(), 0, 0).ConfigureAwait(false);
        }

        protected override async Task BeforeSetLocal()
        {
            if (!string.IsNullOrEmpty(fullPath))
            {
                return;
            }

            string remotePath = $"{SyncService.REMOTE_FILE_FOLDER}/{FileName}";
            string localPath = GetTempLocalFilePath();

            if (!Directory.Exists(SyncService.LOCAL_FILE_FOLDER))
            {
                Directory.CreateDirectory(SyncService.LOCAL_FILE_FOLDER);
            }

            try
            {
                await _webDav.GetFileAsync(remotePath, localPath, 0, 0).ConfigureAwait(false);
                if (GetMD5HashFromFile(localPath) != GetMd5())
                {
                    Log.Write("[PULL] download erro, md5 wrong");
                    statusTip = "Downloading erro, md5 wrong";
                    return;
                }
                Log.Write("[PULL] download OK " + localPath);
                fullPath = localPath;
                statusTip = FileName;
            }
            catch (System.Exception ex)
            {
                statusTip = "";
                Log.Write("[PULL] download file failed " + ex.Message);
            }
        }

        public override string ToolTip()
        {
            return statusTip;
        }

        protected override DataObject CreateDataObject()
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return null;
            }

            var dataObject = new DataObject();

            dataObject.SetFileDropList(new System.Collections.Specialized.StringCollection { fullPath });

            return dataObject;
        }

        public override bool Equals(System.Object obj)
        {
            if (obj is null)
            {
                return false;
            }

            try
            {
                FileProfile profile = (FileProfile)obj;
                return this.GetMd5() == profile.GetMd5();
            }
            catch
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return this.GetMd5().GetHashCode();
        }

        private static string GetMD5HashFromFile(string fileName)
        {
            var fileInfo = new FileInfo(fileName);
            if (fileInfo.Length > maxFileSize)
            {
                return MD5_FOR_OVERSIZED_FILE;
            }
            try
            {
                Log.Write("calc md5 start");
                var file = new FileStream(fileName, FileMode.Open);
                var md5Oper = System.Security.Cryptography.MD5.Create();
                var retVal = md5Oper.ComputeHash(file);
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

        public override Action ExecuteProfile()
        {
            var path = fullPath ?? GetTempLocalFilePath();
            if (path != null)
            {
                return () =>
                {
                    var open = new System.Diagnostics.Process();
                    open.StartInfo.FileName = "explorer";
                    open.StartInfo.Arguments = "/e,/select," + path;
                    open.Start();
                };
            }

            return null;
        }
    }
}
