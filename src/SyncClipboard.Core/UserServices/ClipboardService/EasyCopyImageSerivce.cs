using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Image;
using System.Text.RegularExpressions;

namespace SyncClipboard.Core.UserServices;

public class EasyCopyImageSerivce : ClipboardHander
{
    #region override ClipboardHander

    public override string SERVICE_NAME => I18n.Strings.ImageAssistant;
    public override string LOG_TAG => "EASY IMAGE";

    private const string STOPPED_STATUS = "Stopped.";
    private const string RUNNING_STATUS = "Running.";

    protected override ILogger Logger => _logger;
    protected override IContextMenu? ContextMenu => _serviceProvider.GetRequiredService<IContextMenu>();
    protected override IClipboardChangingListener ClipboardChangingListener
                                                  => _serviceProvider.GetRequiredService<IClipboardChangingListener>();
    protected override bool SwitchOn
    {
        get => _clipboardAssistConfig.EasyCopyImageSwitchOn;
        set
        {
            _clipboardAssistConfig.EasyCopyImageSwitchOn = value;
            _configManager.SetConfig(ConfigKey.ClipboardAssist, _clipboardAssistConfig);
        }
    }

    private bool DownloadWebImageEnabled => _clipboardAssistConfig.DownloadWebImage;

    protected override async void HandleClipboard(ClipboardMetaInfomation meta, CancellationToken cancelToken)
    {
        try
        {
            await ProcessClipboard(meta, false, cancelToken);
        }
        catch (Exception ex)
        {
            Logger.Write(LOG_TAG, ex.Message);
        }
    }

    protected override CancellationToken StopPreviousAndGetNewToken()
    {
        TrayIcon.SetStatusString(SERVICE_NAME, RUNNING_STATUS);
        _progress?.CancelSicent();
        _progress = null;
        return base.StopPreviousAndGetNewToken();
    }

    #endregion override ClipboardHander

    private ProgressToastReporter? _progress;
    private readonly object _progressLocker = new();

    private readonly INotification _notificationManager;
    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly IClipboardFactory _clipboardFactory;
    private readonly IServiceProvider _serviceProvider;
    private ClipboardAssistConfig _clipboardAssistConfig;
    private IHttp Http => _serviceProvider.GetRequiredService<IHttp>();
    private ITrayIcon TrayIcon => _serviceProvider.GetRequiredService<ITrayIcon>();

    public EasyCopyImageSerivce(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>();
        _notificationManager = _serviceProvider.GetRequiredService<INotification>();
        _clipboardAssistConfig = _configManager.GetConfig<ClipboardAssistConfig>();
    }

    public override void Load()
    {
        _clipboardAssistConfig = _configManager.GetConfig<ClipboardAssistConfig>();
        var status = SwitchOn ? RUNNING_STATUS : STOPPED_STATUS;
        TrayIcon.SetStatusString(SERVICE_NAME, status);
        base.Load();
    }

    private async Task ProcessClipboard(ClipboardMetaInfomation metaInfo, bool useProxy, CancellationToken cancellationToken)
    {
        TrayIcon.SetStatusString(SERVICE_NAME, RUNNING_STATUS);
        var profile = _clipboardFactory.CreateProfile(metaInfo);
        if (NeedAdjust(profile, metaInfo) is not true)
        {
            return;
        }

        if (DownloadWebImageEnabled && !string.IsNullOrEmpty(metaInfo.Html))
        {
            const string Expression = @".*<[\s]*img[\s]*.*?[\s]*src=(?<quote>[""'])(?<imgUrl>https?://.*?)\k<quote>.*?/[\s]*>";
            var match = Regex.Match(metaInfo.Html, Expression, RegexOptions.Compiled);    // 性能未测试，benchmark参考 https://www.bilibili.com/video/av441496306/?p=1&plat_id=313&t=15m53s
            if (match.Success) // 是从浏览器复制的图片
            {
                TrayIcon.SetStatusString(SERVICE_NAME, "Downloading web image.");
                _logger.Write(LOG_TAG, "http image url: " + match.Groups["imgUrl"].Value);
                var uri = new Uri(match.Groups["imgUrl"].Value);
                var localPath = await DownloadImage(uri, useProxy, cancellationToken);
                if (!ImageHelper.FileIsImage(localPath))
                {
                    TrayIcon.SetStatusString(SERVICE_NAME, "Converting Complex image.");
                    localPath = await ImageHelper.CompatibilityCast(localPath, Env.TemplateFileFolder, cancellationToken);
                }
                profile = new ImageProfile(localPath, _serviceProvider);
            }
        }

        await AdjustClipboard(profile, cancellationToken);
        TrayIcon.SetStatusString(SERVICE_NAME, RUNNING_STATUS);
    }

    private static bool NeedAdjust(Profile profile, ClipboardMetaInfomation metaInfo)
    {
        if (profile.Type != ProfileType.Image && metaInfo.OriginalType != ClipboardMetaInfomation.ImageType)
        {
            return false;
        }

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
                profile.SetLocalClipboard(false, cancellationToken);
                break;
            }
            catch
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    private async Task<string> DownloadImage(Uri imageUri, bool useProxy, CancellationToken cancellationToken)
    {
        var filename = Regex.Match(imageUri.LocalPath, "[^/]+(?!.*/)");
        lock (_progressLocker)
        {
            _progress ??= new(filename.Value[..Math.Min(filename.Value.Length, 50)], I18n.Strings.DownloadingWebImage, _notificationManager);
        }
        if (useProxy)
        {
            var fullPath = Path.Combine(Env.TemplateFileFolder, "proxy " + filename.Value);
            await Http.GetFile(imageUri.AbsoluteUri, fullPath, _progress, cancellationToken, true);
            return fullPath;
        }
        else
        {
            var fullPath = Path.Combine(Env.TemplateFileFolder, filename.Value);
            await Http.GetFile(imageUri.AbsoluteUri, fullPath, _progress, cancellationToken);
            return fullPath;
        }
    }
}