using System.IO;
using System.Text;
using SyncClipboard.Utility;
using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    class FileProfile : Profile
    {
        private string fullPath;
        private const string folder = "file";
        private const long maxFileSize = 500 * 1024 * 1024;     // 500MBytes

        public FileProfile(string file)
        {
            FileName = System.IO.Path.GetFileName(file);
            fullPath = file;
        }

        protected override ClipboardType GetProfileType()
        {
            return ClipboardType.File;
        }

        public override void UploadProfile()
        {
            string remotePath = Config.GetRemotePath() + $"/{folder}/{FileName}";

            FileInfo file = new FileInfo(fullPath);
            if (file.Length <= maxFileSize)
            {
                Log.Write("PUSH file " + FileName);
                HttpWebResponseUtility.PutFile(remotePath, fullPath, Config.TimeOut, Config.GetHttpAuthHeader());
            }
            else
            {
                Log.Write("file is too large, skipped " + FileName);
            }

            Text = GetMD5HashFromFile(fullPath);
            HttpWebResponseUtility.PutText(Config.GetProfileUrl(), this.ToJsonString(), Config.TimeOut, Config.GetHttpAuthHeader());
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
