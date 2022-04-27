using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SyncClipboard.Module;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Web;
using static SyncClipboard.Service.ProfileFactory;

namespace SyncClipboard.Service
{
    public class EasyCopyImageSerivce : Service
    {
        protected override void StartService()
        {
            Log.Write("EasyCopyImageSerivce started");
        }

        protected override void StopSerivce()
        {
            Log.Write("EasyCopyImageSerivce stopped");
        }

        private void ClipBoardChangedHandler()
        {
            if (UserConfig.Config.SyncService.EasyCopyImageSwitchOn)
            {
                ProcessClipboard();
            }
        }

        private async void ProcessClipboard()
        {
            var profile = CreateFromLocal(out var localClipboard);
            if (profile.GetProfileType() != ProfileType.ClipboardType.Image)
            {
                return;
            }

            if (!string.IsNullOrEmpty(localClipboard.Html)) // 无html，通常是纯复制图片，剪切板中只有bitmap
            {
                var match = Regex.Match(localClipboard.Html, @"<!--StartFragment--><img src=""(http[s]?://.*)""/><!--EndFragment-->");
                if (match.Success) // 是从浏览器复制的图片
                {
                    Log.Write("http image url: " + match.Result("$1"));
                    var localPath = await DownloadImage(match.Result("$1"));
                    if (localPath is null || !SupportsImage(localPath))
                    {
                        return;
                    }
                    profile = new ImageProfile(localPath);
                }
            }

            await AdjustClipboard(profile, localClipboard);
        }

        private bool SupportsImage(string fileName)
        {
            string extension = System.IO.Path.GetExtension(fileName);
            foreach (var imageExtension in imageExtensions)
            {
                if (imageExtension.Equals(extension, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private async Task AdjustClipboard(Profile profile, LocalClipboard localClipboard)
        {
            if (localClipboard.Files is null || localClipboard.Html is null || localClipboard.Image is null)
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        await profile.SetLocalClipboard();
                        break;
                    }
                    catch
                    {
                        await Task.Delay(50);
                    }
                }
            }
        }

        private static async Task<string> DownloadImage(string imageUrl)
        {
            try
            {
                var match = Regex.Match(imageUrl, "[^/]+(?!.*/)");
                var localPath = Path.Combine(SyncService.LOCAL_FILE_FOLDER, match.Value);
                await Http.HttpClient.GetFile(imageUrl, localPath);
                return localPath;
            }
            catch
            {
                Log.Write("Download http image failed");
                return null;
            }
        }

        public override void RegistEventHandler()
        {
            Event.RegistEventHandler(ClipboardService.CLIPBOARD_CHANGED_EVENT_NAME, ClipBoardChangedHandler);
        }

        public override void UnRegistEventHandler()
        {
            Event.UnRegistEventHandler(ClipboardService.CLIPBOARD_CHANGED_EVENT_NAME, ClipBoardChangedHandler);
        }
    }
}