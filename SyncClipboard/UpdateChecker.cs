using System;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SyncClipboard
{
    class UpdateChecker
    {
        public const string Version = "1.1.2";
        public const string UpdateUrl = "https://api.github.com/repos/Jeric-X/SyncClipboard/releases/latest";
        public const string ReleaseUrl = "https://github.com/Jeric-X/SyncClipboard/releases/latest";

        public bool Check()
        {
            String strReply = "";
            try
            {
                strReply = HttpWebResponseUtility.GetText(UpdateUrl, Config.TimeOut, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("网络连接失败", "获取更新信息失败");
                return false;
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            UpdateConvertJson p1 = null;
            try
            {
                p1 = serializer.Deserialize<UpdateConvertJson>(strReply);
                if (String.Compare(p1.name, "v" + Version) > 0){

                
                    if (MessageBox.Show("v" + Version + " -> " + p1.name + "\n\n是否更新", "检测到新版本", MessageBoxButtons.OKCancel) == DialogResult.OK)
                        System.Diagnostics.Process.Start(UpdateChecker.ReleaseUrl);
                }
                else
                {
                    MessageBox.Show("当前版本为最新版本", "更新");
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message.ToString(), "获取更新信息失败");
            }
            return false;
        }
    }
    class UpdateConvertJson
    {
        public String name { get; set; }
        
        public String rowser_download_url { get; set; }
    }
}
