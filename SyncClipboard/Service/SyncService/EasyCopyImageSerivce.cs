using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Module;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Web;
using static SyncClipboard.Service.ProfileFactory;
#nullable enable

namespace SyncClipboard.Service
{
    public class EasyCopyImageSerivce : Service
    {
        private event Action<bool>? SwitchChanged;
        private const string SERVICE_NAME = "💠";
        private const string LOG_TAG = "EASY IMAGE";
        protected override void StartService()
        {
            Log.Write(LOG_TAG, "EasyCopyImageSerivce started");
            SwitchChanged += Global.Menu.AddMenuItemGroup(
                new string[] { "Easy Copy Image" },
                new Action<bool>[] {
                    (switchOn) => {
                        UserConfig.Config.SyncService.EasyCopyImageSwitchOn = switchOn;
                        UserConfig.Save();
                    }
                }
            )[0];
            SwitchChanged?.Invoke(UserConfig.Config.SyncService.EasyCopyImageSwitchOn);
        }

        public override void Load()
        {
            SwitchChanged?.Invoke(UserConfig.Config.SyncService.EasyCopyImageSwitchOn);
        }

        protected override void StopSerivce()
        {
            Log.Write(LOG_TAG, "EasyCopyImageSerivce stopped");
        }

        private CancellationTokenSource? _cancelSource;
        private readonly object _cancelSourceLocker = new();
        private ProgressToastReporter? _progress;
        private readonly object _progressLocker = new();

        private CancellationToken StopPreviousAndGetNewToken()
        {
            _progress?.CancelSicent();
            _progress = null;
            lock (_cancelSourceLocker)
            {
                if (_cancelSource?.Token.CanBeCanceled ?? false)
                {
                    _cancelSource.Cancel();
                }
                _cancelSource = new();
                return _cancelSource.Token;
            }
        }

        private void ClipBoardChangedHandler()
        {
            if (UserConfig.Config.SyncService.EasyCopyImageSwitchOn)
            {
                Global.Notifyer.SetStatusString(SERVICE_NAME, "running");
                CancellationToken cancelToken = StopPreviousAndGetNewToken();
                _ = ProcessClipboard(false, cancelToken);
                _ = ProcessClipboard(true, cancelToken);
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
                var match = Regex.Match(localClipboard.Html, @"<!--StartFragment--><img src=""(http[s]?://.*)""/><!--EndFragment-->");
                if (match.Success) // 是从浏览器复制的图片
                {
                    Log.Write(LOG_TAG, "http image url: " + match.Result("$1"));
                    Global.Notifyer.SetStatusString(SERVICE_NAME, "downloading");
                    var localPath = await DownloadImage(match.Result("$1"), useProxy, cancellationToken);
                    if (!SupportsImage(localPath))
                    {
                        return;
                    }
                    profile = new ImageProfile(localPath);
                }
            }

            await AdjustClipboard(profile, cancellationToken);
        }

        private static bool SupportsImage(string fileName)
        {
            string extension = Path.GetExtension(fileName);
            foreach (var imageExtension in imageExtensions)
            {
                if (imageExtension.Equals(extension, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
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
                    profile.SetLocalClipboard(false);
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