using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SyncClipboard
{
    class UpdateChecker
    {
        public const string Version = "1.1.0";
        public const string UpdateUrl = "https://api.github.com/repos/Jeric-X/SyncClipboard/releases/latest";
        public const string ReleaseUrl = "https://github.com/Jeric-X/SyncClipboard/releases/latest";

        public bool Check()
        {
            HttpWebResponse httpWebResponse = null;
            try
            {
                httpWebResponse = HttpWebResponseUtility.CreateGetHttpResponse(UpdateUrl, Config.TimeOut, null, null, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message.ToString());
                return false;
            }
            StreamReader objStrmReader = new StreamReader(httpWebResponse.GetResponseStream());
            String strReply = objStrmReader.ReadToEnd();
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
                    MessageBox.Show("当前版本为最新版本");
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message.ToString());
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
