using System;
using System.Text.Json;
using System.Windows.Forms;
using SyncClipboard.Utility;

namespace SyncClipboard.Module
{
    internal class UpdateChecker
    {
        public const string Version = Env.VERSION;
        public const int VersionPartNumber = 3;
        public const string UpdateUrl = "https://api.github.com/repos/Jeric-X/SyncClipboard/releases/latest";
        public const string ReleaseUrl = "https://github.com/Jeric-X/SyncClipboard/releases/latest";

        public void Check()
        {
            string newVersion = GetNewestVersion();
            if (newVersion is null)
            {
                return;
            }

            if (NeedUpdate(newVersion))
            {
                if (MessageBox.Show($"v{Version} -> {newVersion}", $"检测到新版本{newVersion}, 是否更新", MessageBoxButtons.OKCancel) == DialogResult.OK)
                {
                    Sys.OpenWithDefaultApp(ReleaseUrl);
                }
            }
            else
            {
                MessageBox.Show("当前版本v" + Version + "为最新版本", "更新");
            }
        }

        private string GetNewestVersion()
        {
            string gitHubReply;
            try
            {
                gitHubReply = HttpWeb.GetText(UpdateUrl, new HttpPara { Timeout = UserConfig.Config.Program.TimeOut });
            }
            catch
            {
                MessageBox.Show("网络连接失败", "获取更新信息失败");
                return null;
            }

            try
            {
                UpdateConvertJson p1 = JsonSerializer.Deserialize<UpdateConvertJson>(gitHubReply);
                return p1.name;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "获取更新信息失败");
            }

            return null;
        }

        private bool NeedUpdate(string newVersionStr)
        {
            newVersionStr = newVersionStr.Substring(1);         // 去除v1.0.0中的v
            string[] newVersion = newVersionStr.Split(new char[2] { 'v', '.' });
            string[] oldVersion = Version.Split(new char[2] { 'v', '.' });

            for (int i = 0; i < VersionPartNumber; i++)
            {
                int newVersionNum = Convert.ToInt32(newVersion[i]);
                int oldVersionNum = Convert.ToInt32(oldVersion[i]);
                if (newVersionNum > oldVersionNum)
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal class UpdateConvertJson
    {
        public string name { get; set; }
    }
}
