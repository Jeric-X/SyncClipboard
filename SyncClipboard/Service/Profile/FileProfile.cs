using System.IO;
using System.Text;
using SyncClipboard.Utility;
using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    class FileProfile : Profile
    {
        private string fullPath;
        private string remotePath;
        private string localPath;
        private const string fileFolder = "file";
        private const long maxFileSize = 500 * 1024 * 1024;     // 500MBytes

        public FileProfile(string file)
        {
            FileName = System.IO.Path.GetFileName(file);
            fullPath = file;
        }

        public FileProfile(JsonProfile jsonProfile)
        {
            FileName = jsonProfile.File;
        }

        protected override ClipboardType GetProfileType()
        {
            return ClipboardType.File;
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

            Text = GetMD5HashFromFile(fullPath);
            HttpWebResponseUtility.PutText(Config.GetProfileUrl(), this.ToJsonString(), Config.GetHttpAuthHeader());
        }

        protected override void BeforeSetLocal()
        {
            remotePath = Config.GetRemotePath() + $"/{fileFolder}/{FileName}";
            localPath = $"{fileFolder}/{FileName}";
        }

        protected override void SetContentToLocalClipboard()
        {
            // TODO
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
