using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.Image;
using SyncClipboard.Core.Utilities.Notification;
using System.Text.RegularExpressions;

namespace SyncClipboard.Core.UserServices;

public class EasyCopyImageSerivce : ClipboardHander
{
    #region override ClipboardHander

    public override string SERVICE_NAME => "Easy Copy Image";
    public override string LOG_TAG => "EASY IMAGE";

    protected override ILogger Logger => _logger;
    protected override IContextMenu? ContextMenu => _serviceProvider.GetRequiredService<IContextMenu>();
    protected override IClipboardChangingListener ClipboardChangingListener
                                                  => _serviceProvider.GetRequiredService<IClipboardChangingListener>();

    protected override bool SwitchOn
    {
        get => _userConfig.Config.SyncService.EasyCopyImageSwitchOn;
        set
        {
            _userConfig.Config.SyncService.EasyCopyImageSwitchOn = value;
            _userConfig.Save();
        }
    }

    protected override void HandleClipboard(ClipboardMetaInfomation meta, CancellationToken cancelToken)
    {
        Task[] tasks = {
            ProcessClipboard(meta, false, cancelToken),
            ProcessClipboard(meta, true, cancelToken)
        };
        foreach (var task in tasks)
        {
            task.ContinueWith((_) => this.CancelProcess(), TaskContinuationOptions.OnlyOnRanToCompletion);
        }
    }

    protected override CancellationToken StopPreviousAndGetNewToken()
    {
        _progress?.CancelSicent();
        _progress = null;
        return base.StopPreviousAndGetNewToken();
    }

    #endregion override ClipboardHander

    private ProgressToastReporter? _progress;
    private readonly object _progressLocker = new();

    private readonly NotificationManager _notificationManager;
    private readonly ILogger _logger;
    private readonly UserConfig _userConfig;
    private readonly IClipboardFactory _clipboardFactory;
    private readonly IServiceProvider _serviceProvider;
    private IHttp Http => _serviceProvider.GetRequiredService<IHttp>();

    public EasyCopyImageSerivce(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _userConfig = _serviceProvider.GetRequiredService<UserConfig>();
        _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>();
        _notificationManager = _serviceProvider.GetRequiredService<NotificationManager>();
    }

    private async Task ProcessClipboard(ClipboardMetaInfomation metaInfo, bool useProxy, CancellationToken cancellationToken)
    {
        var profile = _clipboardFactory.CreateProfile(metaInfo);
        if (profile.Type != ProfileType.Image || !NeedAdjust(metaInfo))
        {
            return;
        }

        if (!string.IsNullOrEmpty(metaInfo.Html))
        {
            const string Expression = @"<!--StartFragment--><img src=(?<qoute>[""'])(?<imgUrl>https?://.*?)\k<qoute>.*/><!--EndFragment-->";
            var match = Regex.Match(metaInfo.Html, Expression, RegexOptions.Compiled);    // 性能未测试，benchmark参考 https://www.bilibili.com/video/av441496306/?p=1&plat_id=313&t=15m53s
            if (match.Success) // 是从浏览器复制的图片
            {
                _logger.Write(LOG_TAG, "http image url: " + match.Groups["imgUrl"].Value);
                var localPath = await DownloadImage(match.Groups["imgUrl"].Value, useProxy, cancellationToken);
                if (!ImageHelper.FileIsImage(localPath))
                {
                    localPath = await ImageHelper.CompatibilityCast(localPath, SyncService.LOCAL_FILE_FOLDER, cancellationToken);
                }
                profile = new ImageProfile(localPath, _serviceProvider);
            }
        }

        await AdjustClipboard(profile, cancellationToken);
    }

    private static bool NeedAdjust(ClipboardMetaInfomation metaInfo)
    {
        if (metaInfo.Files?.Length > 1)
        {
            return false;
        }

        if ((metaInfo.Effects & DragDropEffects.Move) == DragDropEffects.Move)
        {
            return false;
        }
        return metaInfo.Files is null || metaInfo.Html is null || metaInfo.Image is null;
    }

    private static async Task AdjustClipboard(Profile profile, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                profile.SetLocalClipboard();
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
            _progress ??= new(filename.Value[..Math.Min(filename.Value.Length, 50)], "正在从网站下载原图", _notificationManager);
        }
        if (useProxy)
        {
            var fullPath = Path.Combine(SyncService.LOCAL_FILE_FOLDER, "proxy " + filename.Value);
            await Http.GetFile(imageUrl, fullPath, _progress, cancellationToken, true);
            return fullPath;
        }
        else
        {
            var fullPath = Path.Combine(SyncService.LOCAL_FILE_FOLDER, filename.Value);
            await Http.GetFile(imageUrl, fullPath, _progress, cancellationToken);
            return fullPath;
        }
    }
}