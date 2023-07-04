using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SyncClipboard.Module;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Image;
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

        protected override bool SwitchOn
        {
            get => UserConfig.Config.SyncService.EasyCopyImageSwitchOn;
            set
            {
                UserConfig.Config.SyncService.EasyCopyImageSwitchOn = value;
                UserConfig.Save();
            }
        }

        protected override CancellationToken StopPreviousAndGetNewToken()
        {
            _progress?.CancelSicent();
            _progress = null;
            return base.StopPreviousAndGetNewToken();
        }

        protected override void HandleClipboard(CancellationToken cancelToken)
        {
            Task[] tasks = {
                ProcessClipboard(false, cancelToken),
                ProcessClipboard(true, cancelToken)
            };
            foreach (var task in tasks)
            {
                task.ContinueWith((_) => this.CancelProcess(), TaskContinuationOptions.OnlyOnRanToCompletion);
            }
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
                const string Expression = @"<!--StartFragment--><img src=(?<qoute>[""'])(?<imgUrl>https?://.*?)\k<qoute>.*/><!--EndFragment-->";
                var match = Regex.Match(localClipboard.Html, Expression, RegexOptions.Compiled);    // 性能未测试，benchmark参考 https://www.bilibili.com/video/av441496306/?p=1&plat_id=313&t=15m53s
                if (match.Success) // 是从浏览器复制的图片
                {
                    Log.Write(LOG_TAG, "http image url: " + match.Groups["imgUrl"].Value);
                    var localPath = await DownloadImage(match.Groups["imgUrl"].Value, useProxy, cancellationToken);
                    if (!ImageHelper.FileIsImage(localPath))
                    {
                        localPath = await ImageHelper.CompatibilityCast(localPath, SyncService.LOCAL_FILE_FOLDER, cancellationToken);
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

            if ((localClipboard.Effects & DragDropEffects.Move) == DragDropEffects.Move)
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

        private async Task<string> DownloadImage(string imageUrl, bool useProxy, CancellationToken cancellationToken)
        {
            var filename = Regex.Match(imageUrl, "[^/]+(?!.*/)");
            lock (_progressLocker)
            {
                _progress ??= new(filename.Value[..Math.Min(filename.Value.Length, 50)], "正在从网站下载原图");
            }
            if (useProxy)
            {
                var fullPath = Path.Combine(SyncService.LOCAL_FILE_FOLDER, "proxy " + filename.Value);
                await Global.Http.GetFile(imageUrl, fullPath, _progress, cancellationToken, true);
                return fullPath;
            }
            else
            {
                var fullPath = Path.Combine(SyncService.LOCAL_FILE_FOLDER, filename.Value);
                await Global.Http.GetFile(imageUrl, fullPath, _progress, cancellationToken);
                return fullPath;
            }
        }
    }
}