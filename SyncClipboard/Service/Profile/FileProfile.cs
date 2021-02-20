using System.IO;
using System.Text;
using SyncClipboard.Utility;
using static SyncClipboard.ProfileFactory;

namespace SyncClipboard.Service
{
    class FileProfile : Profile
    {
        private string fullPath;
        private const string folder = "file";

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
            //todo 大文件跳过
            string remotePath = Config.GetRemotePath() + $"/{folder}/{FileName}";
            Log.Write("PUSH file " + FileName);
            HttpWebResponseUtility.PutFile(remotePath, fullPath, Config.TimeOut, Config.GetHttpAuthHeader());
            Text = GetMD5HashFromFile(fullPath);
            Log.Write("md5 " + Text);
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
                System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
                byte[] retVal = md5.ComputeHash(file);
                file.Close();

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < retVal.Length; i++)
                {
                    sb.Append(retVal[i].ToString("x2"));
                }
                return sb.ToString();
            }
            catch (System.Exception ex)
            {
                Log.Write("GetMD5HashFromFile() fail " + ex.Message);
                throw ex;
            }
        }
    }
}
