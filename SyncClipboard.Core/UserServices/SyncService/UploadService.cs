using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Notification;

namespace SyncClipboard.Core.UserServices;

public class UploadService : ClipboardHander
{
    public event ProgramEvent.ProgramEventHandler? PushStarted;
    public event ProgramEvent.ProgramEventHandler? PushStopped;

    private const string SERVICE_NAME_SIMPLE = "⬆⬆";
    public override string SERVICE_NAME => "同步剪切板";
    public override string LOG_TAG => "PUSH";

    protected override ILogger Logger => _logger;
    protected override string? ContextMenuGroupName { get; } = SyncService.ContextMenuGroupName;
    protected override IContextMenu? ContextMenu => _serviceProvider.GetRequiredService<IContextMenu>();
    protected override IClipboardChangingListener ClipboardChangingListener => _serviceProvider.GetRequiredService<IClipboardChangingListener>();

    protected override bool SwitchOn
    {
        get => _syncConfig.PushSwitchOn && _syncConfig.SyncSwitchOn;
        set
        {
            _syncConfig.SyncSwitchOn = value;
            _userConfig.SetConfig(ConfigKey.Sync, _syncConfig);
        }
    }

    private bool _downServiceChangingLocal = false;

    private readonly NotificationManager _notificationManager;
    private readonly ILogger _logger;
    private readonly UserConfig2 _userConfig;
    private readonly IClipboardFactory _clipboardFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDav _webDav;
    private readonly ITrayIcon _trayIcon;
    private SyncConfig _syncConfig;

    private IAppConfig AppConfig => _serviceProvider.GetRequiredService<IAppConfig>();

    public UploadService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _userConfig = _serviceProvider.GetRequiredService<UserConfig2>();
        _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>();
        _notificationManager = _serviceProvider.GetRequiredService<NotificationManager>();
        _webDav = _serviceProvider.GetRequiredService<IWebDav>();
        _trayIcon = _serviceProvider.GetRequiredService<ITrayIcon>();
        _syncConfig = _userConfig.GetConfig<SyncConfig>(ConfigKey.Sync) ?? new();
    }

    public override void Load()
    {
        _syncConfig = _userConfig.GetConfig<SyncConfig>(ConfigKey.Sync) ?? new();
        base.Load();
    }

    protected override void StartService()
    {
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Running.");
        base.StartService();
    }

    protected override void StopSerivce()
    {
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Stopped.");
        base.StopSerivce();
    }

    public override void RegistEvent()
    {
        var pushStartedEvent = new ProgramEvent(
            (handler) => PushStarted += handler,
            (handler) => PushStarted -= handler
        );
        Event.RegistEvent(SyncService.PUSH_START_ENENT_NAME, pushStartedEvent);

        var pushStoppedEvent = new ProgramEvent(
            (handler) => PushStopped += handler,
            (handler) => PushStopped -= handler
        );
        Event.RegistEvent(SyncService.PUSH_STOP_ENENT_NAME, pushStoppedEvent);
    }

    public override void RegistEventHandler()
    {
        Event.RegistEventHandler(SyncService.PULL_START_ENENT_NAME, PullStartedHandler);
        Event.RegistEventHandler(SyncService.PULL_STOP_ENENT_NAME, PullStoppedHandler);
        base.RegistEventHandler();
    }

    public override void UnRegistEventHandler()
    {
        Event.UnRegistEventHandler(SyncService.PULL_START_ENENT_NAME, PullStartedHandler);
        Event.UnRegistEventHandler(SyncService.PULL_STOP_ENENT_NAME, PullStoppedHandler);
        base.UnRegistEventHandler();
    }

    public void PullStartedHandler()
    {
        _logger.Write("_isChangingLocal set to TRUE");
        _downServiceChangingLocal = true;
    }

    public void PullStoppedHandler()
    {
        _logger.Write("_isChangingLocal set to FALSE");
        _downServiceChangingLocal = false;
    }

    private void SetWorkingStartStatus()
    {
        _trayIcon.ShowUploadAnimation();
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Uploading.");
        PushStarted?.Invoke();
    }

    private void SetWorkingEndStatus()
    {
        _trayIcon.StopAnimation();
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Running.", false);
        PushStopped?.Invoke();
    }

    protected override async void HandleClipboard(ClipboardMetaInfomation meta, CancellationToken cancellationToken)
    {
        if (_downServiceChangingLocal)
        {
            return;
        }

        SetWorkingStartStatus();
        try
        {
            await UploadClipboard(meta, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.Write("Upload", "Upload Canceled");
        }
        SetWorkingEndStatus();
    }

    private async Task UploadClipboard(ClipboardMetaInfomation meta, CancellationToken cancelToken)
    {
        var currentProfile = _clipboardFactory.CreateProfile(meta);

        if (currentProfile.Type == ProfileType.Unknown)
        {
            _logger.Write("Local profile type is Unkown, stop upload.");
            return;
        }

        await UploadLoop(currentProfile, cancelToken);
    }

    private async Task UploadLoop(Profile profile, CancellationToken cancelToken)
    {
        string errMessage = "";
        for (int i = 0; i < _syncConfig.RetryTimes; i++)
        {
            try
            {
                SyncService.remoteProfilemutex.WaitOne();
                var remoteProfile = await _clipboardFactory.CreateProfileFromRemote(cancelToken);
                if (!await Profile.Same(remoteProfile, profile, cancelToken))
                {
                    await CleanServerTempFile(cancelToken);
                    await profile.UploadProfile(_webDav, cancelToken);
                }
                _logger.Write(LOG_TAG, "remote is same as local, won't push");
                return;
            }
            catch (TaskCanceledException)
            {
                cancelToken.ThrowIfCancellationRequested();
                _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, $"失败，正在第{i + 1}次尝试，错误原因：请求超时", true);
                errMessage = "连接超时";
            }
            catch (Exception ex)
            {
                errMessage = ex.Message;
                _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, $"失败，正在第{i + 1}次尝试，错误原因：{errMessage}", true);
            }
            finally
            {
                SyncService.remoteProfilemutex.ReleaseMutex();
            }

            await Task.Delay(TimeSpan.FromSeconds(_syncConfig.IntervalTime), cancelToken);
        }
        _notificationManager.SendText("上传失败：" + profile.ToolTip(), errMessage);
    }

    private async Task CleanServerTempFile(CancellationToken cancelToken)
    {
        if (_syncConfig.DeletePreviousFilesOnPush)
        {
            try
            {
                await _webDav.Delete(AppConfig.RemoteFileFolder, cancelToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound)  // 如果文件夹不存在直接忽略
            {
            }
        }
    }
}