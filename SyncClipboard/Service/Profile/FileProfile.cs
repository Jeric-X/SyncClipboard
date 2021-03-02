using System.IO;
using System.Text;
using SyncClipboard.Utility;
using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    class FileProfile : Profile
    {
        private string fullPath;
        private bool DownloadStatusOK = false;
        private const string fileFolder = "file";
        private string tempFilePath = System.Windows.Forms.Application.StartupPath + $"/{fileFolder}";
        private const long maxFileSize = 500 * 1024 * 1024;     // 500MBytes
        private string statusTip ="";

        public FileProfile(string file)
        {
            FileName = System.IO.Path.GetFileName(file);
            fullPath = file;
        }

        public FileProfile(JsonProfile jsonProfile)
        {
            FileName = jsonProfile.File;
            SetMd5(jsonProfile.Clipboard);
        }

        protected string GetTempLocalFilePath()
        {
            return tempFilePath + "/" + FileName;
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
            if(string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(fullPath))
            {
                SetMd5(GetMD5HashFromFile(fullPath));
            }
            return Text;
        }

        public override void UploadProfile()
        {
            string remotePath = Config.GetRemotePath() + $"/{fileFolder}/{FileName}";

            FileInfo file = new FileInfo(fullPath);
            if (file.Length <= maxFileSize)
            {
                Log.Write("PUSH file " + FileName);
                HttpWebResponseUtility.PutFile(remotePath, fullPath, Config.GetHttpAuthHeader());
            }
            else
            {
                Log.Write("file is too large, skipped " + FileName);
            }

            SetMd5(GetMD5HashFromFile(fullPath));
            HttpWebResponseUtility.PutText(Config.GetProfileUrl(), this.ToJsonString(), Config.GetHttpAuthHeader());
        }

        protected override void BeforeSetLocal()
        {
            string remotePath = Config.GetRemotePath() + $"/{fileFolder}/{FileName}";
            string localPath = GetTempLocalFilePath();

            if (Directory.Exists(fileFolder) == false)
            {
                Directory.CreateDirectory(fileFolder);
            }

            try
            {
                HttpWebResponseUtility.Getfile(remotePath, localPath, Config.GetHttpAuthHeader());
                DownloadStatusOK = true;
                if (GetMD5HashFromFile(localPath) != GetMd5())
                {
                    Log.Write("[PULL] download erro, md5 wrong");
                    statusTip = "Downloading erro, md5 wrong";
                    return;
                }
                Log.Write("[PULL] download OK " + localPath);
                DownloadStatusOK = true;
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

        protected override void SetContentToLocalClipboard()
        {
            if (!DownloadStatusOK)
            {
                return;
            }

            string localPath = GetTempLocalFilePath();
            System.Windows.Forms.Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection{ localPath });

            return;
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
            try
            {
                Log.Write("calc md5 start");
                FileStream file = new FileStream(fileName, FileMode.Open);
                System.Security.Cryptography.MD5 md5Oper = new System.Security.Cryptography.MD5CryptoServiceProvider();
                byte[] retVal = md5Oper.ComputeHash(file);
                file.Close();

                StringBuilder sb = new StringBuilder();
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
                throw ex;
            }
        }
    }
}
