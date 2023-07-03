using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Utility;
#nullable enable

namespace SyncClipboard.Module
{
    internal static class UpdateChecker
    {
        public const string Version = Env.VERSION;
        private const string GITHUB_JSON_VERSION_TAG = "name";
        public const int VersionPartNumber = 3;
        public const string UpdateUrl = "https://api.github.com/repos/Jeric-X/SyncClipboard/releases/latest";
        public const string ReleaseUrl = "https://github.com/Jeric-X/SyncClipboard/releases/latest";

        public static async void Check()
        {
            var newVersion = await GetNewestVersion();
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

        private static async Task<string?> GetNewestVersion()
        {
            string gitHubReply;
            try
            {
                gitHubReply = await Global.Http.GetHttpClient().GetStringAsync(UpdateUrl);
            }
            catch
            {
                MessageBox.Show("网络连接失败", "获取更新信息失败");
                return null;
            }

            try
            {
                return JsonNode.Parse(gitHubReply)?[GITHUB_JSON_VERSION_TAG]?.GetValue<string>();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "获取更新信息失败");
            }

            return null;
        }

        private static bool NeedUpdate(string newVersionStr)
        {
            newVersionStr = newVersionStr[1..];         // 去除v1.0.0中的v
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
}
