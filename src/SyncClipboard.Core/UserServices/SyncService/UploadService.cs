using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SharpHook;
using SharpHook.Native;
using SyncClipboard.Abstract;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.UserServices;

public class UploadService : ClipboardHander
{
    public event ProgramEvent.ProgramEventHandler? PushStarted;
    public event ProgramEvent.ProgramEventHandler? PushStopped;

    private static readonly Guid CopyAndQuickUploadGuid = Guid.Parse("D13672E9-D14C-4D48-847E-10B030F4B608");
    public UniqueCommand QuickUploadCommand => new UniqueCommand(
        I18n.Strings.UploadOnce,
        Guid.Parse("D0EDB9A4-3409-4A76-BC2B-4C0CD80DD850"),
        QuickUpload
    );
    public UniqueCommand CopyAndQuickUploadCommand => new UniqueCommand(
        I18n.Strings.CopyAndUpload,
        CopyAndQuickUploadGuid,
        CopyAndQuickUpload
    );

    private readonly static string SERVICE_NAME_SIMPLE = I18n.Strings.UploadService;
    public override string SERVICE_NAME => I18n.Strings.ClipboardSyncing;
    public override string LOG_TAG => "PUSH";

    protected override ILogger Logger => _logger;
    protected override string? ContextMenuGroupName { get; } = SyncService.ContextMenuGroupName;
    protected override IContextMenu? ContextMenu => _serviceProvider.GetRequiredService<IContextMenu>();
    protected override IClipboardChangingListener ClipboardChangingListener => _serviceProvider.GetRequiredService<IClipboardChangingListener>();

    protected override bool SwitchOn
    {
        get => _syncConfig.PushSwitchOn && _syncConfig.SyncSwitchOn && (!_serverConfig.ClientMixedMode || !_serverConfig.SwitchOn);
        set
        {
            _syncConfig.SyncSwitchOn = value;
            _configManager.SetConfig(_syncConfig);
        }
    }

    private bool NotifyOnManualUpload => _syncConfig.NotifyOnManualUpload;

    private bool _downServiceChangingLocal = false;
    private Profile? _profileCache;

    private readonly INotification _notificationManager;
    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly IClipboardFactory _clipboardFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDav _webDav;
    private readonly ITrayIcon _trayIcon;
    private readonly IMessenger _messenger;
    private readonly IEventSimulator _keyEventSimulator;
    private readonly HotkeyManager _hotkeyManager;
    private SyncConfig _syncConfig;
    private ServerConfig _serverConfig;

    public UploadService(
        IServiceProvider serviceProvider,
        IMessenger messenger,
        IEventSimulator keyEventSimulator,
        HotkeyManager hotkeyManager)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>();
        _notificationManager = _serviceProvider.GetRequiredService<INotification>();
        _webDav = _serviceProvider.GetRequiredService<IWebDav>();
        _trayIcon = _serviceProvider.GetRequiredService<ITrayIcon>();
        _messenger = messenger;
        _syncConfig = _configManager.GetConfig<SyncConfig>();
        _serverConfig = _configManager.GetConfig<ServerConfig>();
        _keyEventSimulator = keyEventSimulator;
        _hotkeyManager = hotkeyManager;
    }

    public override void Load()
    {
        _syncConfig = _configManager.GetConfig<SyncConfig>();
        _serverConfig = _configManager.GetConfig<ServerConfig>();
        if (!SwitchOn)
        {
            _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Stopped.");
        }
        else
        {
            _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Running.");
        }
        base.Load();
    }

    protected override void StartService()
    {
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
        _messenger.Register<EmptyMessage, string>(this, SyncService.PULL_START_ENENT_NAME, PullStartedHandler);
        _messenger.Register<Profile, string>(this, SyncService.PULL_STOP_ENENT_NAME, PullStoppedHandler);
        base.RegistEventHandler();
    }

    public override void UnRegistEventHandler()
    {
        _messenger.UnregisterAll(this);
        base.UnRegistEventHandler();
    }

    public void PullStartedHandler(object _, EmptyMessage _1)
    {
        _logger.Write("_isChangingLocal set to TRUE");
        _downServiceChangingLocal = true;
    }

    public void PullStoppedHandler(object _, Profile profile)
    {
        _logger.Write("_isChangingLocal set to FALSE");
        _profileCache = profile;
        _downServiceChangingLocal = false;
    }

    private void SetWorkingStartStatus()
    {
        _trayIcon.ShowUploadAnimation();
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Uploading.");
    }

    private void SetWorkingEndStatus()
    {
        _trayIcon.StopAnimation();
    }

    private bool IsDownloadServiceWorking(Profile profile)
    {
        if (Profile.Same(profile, _profileCache))
        {
            _logger.Write(LOG_TAG, "Same as lasted downloaded profile, won't push.");
            _profileCache = null;
            return true;
        }

        return _downServiceChangingLocal;
    }

    private async Task<bool> IsObsoleteMeta(ClipboardMetaInfomation meta, CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }
        var latest = await _clipboardFactory.GetMetaInfomation(token);
        return latest != meta;
    }

    protected override async Task HandleClipboard(ClipboardMetaInfomation meta, Profile profile, CancellationToken token)
    {
        PushStarted?.Invoke();
        using var guard = new ScopeGuard(() => PushStopped?.Invoke());

        await SyncService.remoteProfilemutex.WaitAsync(token);
        try
        {
            if (IsDownloadServiceWorking(profile) || await IsObsoleteMeta(meta, token))
            {
                _logger.Write(LOG_TAG, "Stop Push.");
                return;
            }

            SetWorkingStartStatus();
            using var workingStatusGuard = new ScopeGuard(SetWorkingEndStatus);
            await UploadClipboard(profile, token);
        }
        catch (OperationCanceledException)
        {
            _logger.Write("Upload", "Upload Canceled");
        }
        finally
        {
            SyncService.remoteProfilemutex.Release();
        }
    }

    private async Task UploadClipboard(Profile currentProfile, CancellationToken cancelToken)
    {
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
                var remoteProfile = await _clipboardFactory.CreateProfileFromRemote(cancelToken);
                if (!Profile.Same(remoteProfile, profile))
                {
                    _logger.Write(LOG_TAG, "Start: " + profile.ToJsonString());
                    await CleanServerTempFile(cancelToken);
                    await profile.UploadProfile(_webDav, cancelToken);
                }
                else
                {
                    _logger.Write(LOG_TAG, "Remote is same as local, won't push.");
                }
                _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Running.", false);
                return;
            }
            catch (TaskCanceledException)
            {
                cancelToken.ThrowIfCancellationRequested();
                _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, string.Format(I18n.Strings.UploadFailedStatusTimeout, i + 1), true);
                errMessage = I18n.Strings.Timeout;
            }
            catch (Exception ex)
            {
                errMessage = ex.Message;
                _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, string.Format(I18n.Strings.UploadFailedStatus, i + 1, errMessage), true);
            }

            await Task.Delay(TimeSpan.FromSeconds(_syncConfig.IntervalTime), cancelToken);
        }
        var status = profile.ToolTip();
        _notificationManager.SendText(I18n.Strings.FailedToUpload + status, errMessage);
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, $"{I18n.Strings.FailedToUpload}{status[..Math.Min(status.Length, 200)]}\n{errMessage}", true);
    }

    private async Task CleanServerTempFile(CancellationToken cancelToken)
    {
        if (_syncConfig.DeletePreviousFilesOnPush)
        {
            try
            {
                await _webDav.DirectoryDelete(Env.RemoteFileFolder, cancelToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound)  // 如果文件夹不存在直接忽略
            {
            }
            await _webDav.CreateDirectory(Env.RemoteFileFolder, cancelToken);
        }
    }

    private async void QuickUpload()
    {
        var token = StopPreviousAndGetNewToken();
        try
        {
            var meta = await _clipboardFactory.GetMetaInfomation(token);
            var profile = await _clipboardFactory.CreateProfileFromMeta(meta, token);
            await HandleClipboard(meta, profile, token);
            if (NotifyOnManualUpload)
                _notificationManager.SendTemporary(
                    new NotificationPara(I18n.Strings.Uploaded, profile.ShowcaseText())
                    {
                        Duration = TimeSpan.FromSeconds(4)
                    }
                );
        }
        catch { }
    }

    private async void CopyAndQuickUpload()
    {
        await Task.Run(() =>
        {
            if (_hotkeyManager.HotkeyStatusMap.TryGetValue(CopyAndQuickUploadGuid, out var status))
            {
                status.Hotkey?.Keys.ForEach(key => _keyEventSimulator.SimulateKeyRelease(KeyCodeMap.MapReverse[key]));
            }

            KeyCode modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;

            _keyEventSimulator.SimulateKeyPress(modifier);
            _keyEventSimulator.SimulateKeyPress(KeyCode.VcC);

            _keyEventSimulator.SimulateKeyRelease(KeyCode.VcC);
            _keyEventSimulator.SimulateKeyRelease(modifier);
        });
        await Task.Delay(200);
        QuickUpload();
    }
}