using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.UserServices.ClipboardService;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Image;
using SyncClipboard.Core.ViewModels;
using System.Text.RegularExpressions;

namespace SyncClipboard.Core.UserServices;

public partial class EasyCopyImageSerivce : ClipboardHander
{
    #region override ClipboardHander

    public override string SERVICE_NAME => I18n.Strings.EasyCopyImage;
    public override string LOG_TAG => "EASY IMAGE";

    private const string STOPPED_STATUS = "Stopped.";
    private const string RUNNING_STATUS = "Running.";

    protected override bool SwitchOn
    {
        get => _clipboardAssistConfig.EasyCopyImageSwitchOn || _clipboardAssistConfig.DownloadWebImage;
        set
        {
            _clipboardAssistConfig.EasyCopyImageSwitchOn = value;
            _configManager.SetConfig(_clipboardAssistConfig);
        }
    }

    protected override bool ToggleMenuSwitchOn { get => _clipboardAssistConfig.EasyCopyImageSwitchOn; set => SwitchOn = value; }

    private bool DownloadWebImageEnabled => _clipboardAssistConfig.DownloadWebImage;

    protected override async Task HandleClipboard(ClipboardMetaInfomation meta, Profile profile, CancellationToken cancelToken)
    {
        try
        {
            await ProcessClipboard(meta, profile, cancelToken);
        }
        catch (Exception ex)
        {
            await Logger.WriteAsync(LOG_TAG, ex.Message);
        }
    }

    protected override CancellationToken StopPreviousAndGetNewToken()
    {
        TrayIcon.SetStatusString(SERVICE_NAME, RUNNING_STATUS);
        ProgressToastReporter? progress;
        lock (_progressLocker)
        {
            progress = _progress;
            _progress = null;
        }
        progress?.CancelSicent();
        return base.StopPreviousAndGetNewToken();
    }

    #endregion override ClipboardHander

    #region Hotkey
    private UniqueCommandCollection CommandCollection => new(PageDefinition.CliboardAssistant.Title, PageDefinition.CliboardAssistant.FontIcon!)
    {
        Commands = {
            new UniqueCommand(
                I18n.Strings.SwitchEasyCopyImage,
                "337275BE-57A2-2E97-6096-FF3D087D8A9C",
                () => SwitchEasyCopyImage(!_clipboardAssistConfig.EasyCopyImageSwitchOn)
            )
        }
    };

    private void SwitchEasyCopyImage(bool isOn)
    {
        _configManager.SetConfig(_clipboardAssistConfig with { EasyCopyImageSwitchOn = isOn });
        var notification = _notificationManager.Shared;
        notification.Title = isOn ? I18n.Strings.SwitchOnEasyCopyImage : I18n.Strings.SwitchOffEasyCopyImage;
        notification.Show(new NotificationDeliverOption { Duration = TimeSpan.FromSeconds(2) });
    }
    #endregion Hotkey

    private ProgressToastReporter? _progress;
    private readonly object _progressLocker = new();

    private readonly INotificationManager _notificationManager;
    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalClipboardSetter _localClipboardSetter;
    private ClipboardAssistConfig _clipboardAssistConfig;
    private ClipboardOwnerFilterConfig _easyCopyImageFilterConfig = new();
    private IHttp Http => _serviceProvider.GetRequiredService<IHttp>();
    private ITrayIcon TrayIcon => _serviceProvider.GetRequiredService<ITrayIcon>();

    public EasyCopyImageSerivce(IServiceProvider serviceProvider, LocalClipboardSetter localClipboardSetter)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _notificationManager = _serviceProvider.GetRequiredService<INotificationManager>();
        _clipboardAssistConfig = _configManager.GetConfig<ClipboardAssistConfig>();
        _easyCopyImageFilterConfig = _configManager.GetConfig<ClipboardOwnerFilterConfig>(ConfigKey.EasyCopyImageFilter) ?? new();
        _localClipboardSetter = localClipboardSetter;

        serviceProvider.GetService<HotkeyManager>()?.RegisterCommands(CommandCollection);
    }

    public override void Load()
    {
        _clipboardAssistConfig = _configManager.GetConfig<ClipboardAssistConfig>();
        _easyCopyImageFilterConfig = _configManager.GetConfig<ClipboardOwnerFilterConfig>(ConfigKey.EasyCopyImageFilter) ?? new();
        var status = SwitchOn ? RUNNING_STATUS : STOPPED_STATUS;
        TrayIcon.SetStatusString(SERVICE_NAME, status);
        base.Load();
    }

    private async Task ProcessClipboard(ClipboardMetaInfomation metaInfo, Profile profile, CancellationToken cancellationToken)
    {
        TrayIcon.SetStatusString(SERVICE_NAME, RUNNING_STATUS);
        if (NeedAdjust(profile, metaInfo) is not true)
        {
            return;
        }

        bool shouldAjust = false;
        if (DownloadWebImageEnabled && !string.IsNullOrEmpty(metaInfo.Html))
        {
            var downloadedImageProfile = await ProcessImageFromWeb(metaInfo, cancellationToken);
            shouldAjust = downloadedImageProfile is not null;
            profile = downloadedImageProfile ?? profile;
        }

        var shouldFilter = ClipboardOwnerFilterHelper.ShouldFilter(_easyCopyImageFilterConfig, metaInfo.Owner);
        shouldAjust = shouldAjust || (_clipboardAssistConfig.EasyCopyImageSwitchOn && !shouldFilter);
        if (shouldAjust)
        {
            await AdjustClipboard(profile, cancellationToken);
        }
        TrayIcon.SetStatusString(SERVICE_NAME, RUNNING_STATUS);
    }

    private static bool IsNotImage(Profile profile, ClipboardMetaInfomation metaInfo)
    {
        return profile.Type == ProfileType.Text
            || (profile.Type != ProfileType.Image && metaInfo.OriginalType != ClipboardMetaInfomation.ImageType)
            || (metaInfo.OriginalType is not null && metaInfo.OriginalType != ClipboardMetaInfomation.ImageType);
    }

    private static bool IsFocusOnFileOperation(Profile _, ClipboardMetaInfomation metaInfo)
    {
        return metaInfo.Files?.Length > 1
            || (metaInfo.Effects & DragDropEffects.Move) == DragDropEffects.Move;
    }

    // 在Linux上不借助第三方工具的情况时，Image很容易获取失败
    private static bool IsAreadyAdjustButGetImageFailed(Profile _, ClipboardMetaInfomation metaInfo)
    {
        return metaInfo.Image is null
            && metaInfo.Files is not null
            && metaInfo.Html is not null
            && metaInfo.OriginalType is ClipboardMetaInfomation.ImageType;
    }

    private static bool IsAreadyAdjust(Profile _, ClipboardMetaInfomation metaInfo)
    {
        return metaInfo.Image is not null
            && metaInfo.Files is not null
            && metaInfo.Html is not null;
    }

    private static bool NeedAdjust(Profile profile, ClipboardMetaInfomation metaInfo)
    {
        Func<Profile, ClipboardMetaInfomation, bool>[] checkList =
        [
            IsNotImage,
            IsFocusOnFileOperation,
            IsAreadyAdjustButGetImageFailed,
            IsAreadyAdjust,
        ];

        foreach (var checkFunc in checkList)
        {
            if (checkFunc(profile, metaInfo))
            {
                return false;
            }
        }
        return true;
    }

    private async Task AdjustClipboard(Profile profile, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await _localClipboardSetter.Set(profile, cancellationToken);
                break;
            }
            catch
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    private async Task<Profile?> ProcessImageFromWeb(ClipboardMetaInfomation metaInfo, CancellationToken ctk)
    {
        var match = RegexUrl().Match(metaInfo.Html!);    // 性能未测试，benchmark参考 https://www.bilibili.com/video/av441496306/?p=1&plat_id=313&t=15m53s
        if (match.Success) // 是从浏览器复制的图片
        {
            TrayIcon.SetStatusString(SERVICE_NAME, "Downloading web image.");
            await _logger.WriteAsync(LOG_TAG, "http image url: " + match.Groups["imgUrl"].Value);

            try
            {
                var localPath = await DownloadImage(new Uri(match.Groups["imgUrl"].Value), ctk);
                if (!ImageHelper.FileIsImage(localPath))
                {
                    TrayIcon.SetStatusString(SERVICE_NAME, "Converting Complex image.");
                    localPath = await ConvertService.CompatibilityCast(_serviceProvider, localPath, ctk);
                }
                return new ImageProfile(localPath);
            }
            catch
            {
                ctk.ThrowIfCancellationRequested();
            }
        }
        return null;
    }

    private async Task<string> DownloadImage(Uri imageUri, CancellationToken token)
    {
        using var downloadingCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, downloadingCts.Token);

        var filename = RegexFilename().Match(imageUri.LocalPath);
        ProgressToastReporter progress;
        lock (_progressLocker)
        {
            _progress ??= new ProgressToastReporter(
                SERVICE_NAME,
                filename.Value[..Math.Min(filename.Value.Length, 50)],
                I18n.Strings.DownloadingWebImage,
                buttons: new ActionButton(I18n.Strings.Cancel, downloadingCts.Cancel)
            );
            progress = _progress;
        }

        var fullPath = Path.Combine(Env.TemplateFileFolder, filename.Value);
        try
        {
            await Http.GetFile(imageUri.AbsoluteUri, fullPath, progress, linkedCts.Token);
            return fullPath;
        }
        finally
        {
            lock (_progressLocker)
            {
                if (ReferenceEquals(_progress, progress))
                {
                    _progress = null;
                }
            }
            progress.CancelSicent();
        }
    }

    [GeneratedRegex("[^/]+(?!.*/)")]
    private static partial Regex RegexFilename();
    [GeneratedRegex(@".*<[\s]*img[\s]*.*?[\s]*src=(?<quote>[""'])(?<imgUrl>https?://.*?)\k<quote>.*?/[\s]*>", RegexOptions.Compiled)]
    private static partial Regex RegexUrl();
}
