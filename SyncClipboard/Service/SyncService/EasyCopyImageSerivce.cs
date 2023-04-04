using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Module;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Image;
using SyncClipboard.Utility.Web;
using static SyncClipboard.Service.ProfileFactory;
#nullable enable

namespace SyncClipboard.Service
{
    public class EasyCopyImageSerivce : ClipboardHander
    {
        private ProgressToastReporter? _progress;
        private readonly object _progressLocker = new();

        public override string SERVICE_NAME => "Easy Copy Image";

        public override string LOG_TAG => "EASY IMAGE";

        protected override void MenuItemChanged(bool check)
        {
            UserConfig.Config.SyncService.EasyCopyImageSwitchOn = check;
        }

        protected override void LoadFromConfig(Action<bool> switchOn)
        {
            switchOn(UserConfig.Config.SyncService.EasyCopyImageSwitchOn);
        }

        protected override CancellationToken StopPreviousAndGetNewToken()
        {
            _progress?.CancelSicent();
            _progress = null;
            return base.StopPreviousAndGetNewToken();
        }

        protected override Task HandleClipboard(CancellationToken cancelToken)
        {
            _ = ProcessClipboard(false, cancelToken);
            _ = ProcessClipboard(true, cancelToken);
            return Task.CompletedTask;
        }

        private async Task ProcessClipboard(bool useProxy, CancellationToken cancellationToken)
        {
            var profile = CreateFromLocal(out var localClipboard);
            if (profile.GetProfileType() != ProfileType.ClipboardType.Image || !NeedAdjust(localClipboard))
            {
                return;
            }

            if (!string.IsNullOrEmpty(localClipboard.Html))
            {
                var match = Regex.Match(localClipboard.Html, @"<!--StartFragment--><img src=""(http[s]?://.*)""/><!--EndFragment-->");
                if (match.Success) // 是从浏览器复制的图片
                {
                    Log.Write(LOG_TAG, "http image url: " + match.Result("$1"));
                    var localPath = await DownloadImage(match.Result("$1"), useProxy, cancellationToken);
                    if (!ImageHelper.FileIsImage(localPath))
                    {
                        return;
                    }
                    profile = new ImageProfile(localPath);
                }
            }

            await AdjustClipboard(profile, cancellationToken);
        }

        private static bool NeedAdjust(LocalClipboard localClipboard)
        {
            if (localClipboard.Files?.Length > 1)
            {
                return false;
            }
            return localClipboard.Files is null || localClipboard.Html is null || localClipboard.Image is null;
        }

        private static async Task AdjustClipboard(Profile profile, CancellationToken cancellationToken)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    profile.SetLocalClipboard(null, false);
                    break;
                }
                catch
                {
                    await Task.Delay(50, cancellationToken);
                }
            }
        }

        private async Task<string> DownloadImage(string imageUrl, bool userProxy, CancellationToken cancellationToken)
        {
            var filename = Regex.Match(imageUrl, "[^/]+(?!.*/)");
            lock (_progressLocker)
            {
                _progress ??= new(filename.Value[..Math.Min(filename.Value.Length, 50)], "正在从网站下载原图");
            }
            if (userProxy)
            {
                var fullPath = Path.Combine(SyncService.LOCAL_FILE_FOLDER, "proxy " + filename.Value);
                await Http.HttpClientProxy.GetFile(imageUrl, fullPath, _progress, cancellationToken);
                return fullPath;
            }
            else
            {
                var fullPath = Path.Combine(SyncService.LOCAL_FILE_FOLDER, filename.Value);
                await Http.HttpClient.GetFile(imageUrl, fullPath, _progress, cancellationToken);
                return fullPath;
            }
        }
    }
}