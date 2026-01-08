using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SharpHook;
using SharpHook.Native;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Core.UserServices;

public class DownloadService : Service
{
    private readonly static string SERVICE_NAME = I18n.Strings.DownloadService;
    private const string LOG_TAG = "PULL";
    private bool _isEventDrivenModeActive = false;
    private bool _isQuickDownload = false;
    private bool _isQuickDownloadAndPaste = false;
    private readonly object _serviceStateLocker = new();
    private ProgressToastReporter? _toastReporter;
    private Profile? _remoteProfileCache;
    private Profile? _localProfileCache;
    private readonly SingletonTask _singleDownloadTask = new();
    private int _nonServerErrorTimes = 0;

    private bool _downServiceChangingLocal = false;

    private readonly INotificationManager _notificationManager;
    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly IClipboardFactory _clipboardFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalClipboardSetter _localClipboardSetter;
    private readonly IClipboardChangingListener _clipboardListener;
    private readonly IClipboardMoniter _clipboardMoniter;
    private readonly ITrayIcon _trayIcon;
    private readonly IMessenger _messenger;
    private readonly IEventSimulator _keyEventSimulator;
    private readonly HotkeyManager _hotkeyManager;
    private readonly UploadService _uploadService;
    private readonly RemoteClipboardServerFactory _remoteClipboardServerFactory;
    private readonly HistoryManager _historyManager;
    private readonly ProfileNotificationHelper _clipboardNotificationHelper;
    private SyncConfig _syncConfig;
    private ServerConfig _serverConfig;

    private bool SwitchOn => _syncConfig.SyncSwitchOn && _syncConfig.PullSwitchOn;
    private bool ClientSwitchOn => _syncConfig.SyncSwitchOn;

    #region Hotkey
    private static readonly string QuickDownloadAndPasteGuid = "8a4a033e-31da-1b87-76ea-548885866b66";

    private UniqueCommandCollection CommandCollection => new(PageDefinition.SyncSetting.Title, PageDefinition.SyncSetting.FontIcon!)
    {
        Commands = {
            new UniqueCommand(
                I18n.Strings.SwitchClipboardSyncing,
                "26D8A39E-F50D-CC71-FE15-647F67FDB2F9",
                () => SwitchClipboardSyncing(!_syncConfig.SyncSwitchOn)
            ),
            new UniqueCommand(
                I18n.Strings.SwitchBuiltInServer,
                "145740F4-03F7-6F6C-5B93-B027C7C49C59",
                () => SwitchBuiltInServer(!_serverConfig.SwitchOn)
            ),
            _uploadService.QuickUploadCommand,
            _uploadService.QuickUploadWithoutFilterCommand,
            new UniqueCommand(
                I18n.Strings.DownloadOnce,
                "95396FFF-E5FE-45D3-9D70-4A43FA34FF31",
                QuickDownload
            ),
            _uploadService.CopyAndQuickUploadCommand,
            _uploadService.CopyAndQuickUploadWithoutFilterCommand,
            new UniqueCommand(
                I18n.Strings.DownloadAndPaste,
                QuickDownloadAndPasteGuid,
                QuickDownloadAndPaste
            ),
        }
    };

    private void SwitchClipboardSyncing(bool isOn)
    {
        _configManager.SetConfig(_syncConfig with { SyncSwitchOn = isOn });
        var notification = _notificationManager.Shared;
        notification.Title = isOn ? I18n.Strings.SwitchOnClipboardSyncing : I18n.Strings.SwitchOffClipboardSyncing;
        notification.Show(new NotificationDeliverOption { Duration = TimeSpan.FromSeconds(2) });
    }

    private void SwitchBuiltInServer(bool isOn)
    {
        _configManager.SetConfig(_serverConfig with { SwitchOn = isOn });
        var notification = _notificationManager.Shared;
        notification.Title = isOn ? I18n.Strings.SwitchOnBuiltInServer : I18n.Strings.SwitchOffBuiltInServer;
        notification.Show(new NotificationDeliverOption { Duration = TimeSpan.FromSeconds(2) });
    }

    #endregion

    public DownloadService(
    IServiceProvider serviceProvider,
        IMessenger messenger,
        UploadService uploadService,
        IEventSimulator keyEventSimulator,
        IClipboardMoniter clipboardMoniter,
        IClipboardChangingListener clipboardChangingListener,
        HotkeyManager hotkeyManager,
        RemoteClipboardServerFactory remoteClipboardServerFactory,
        ProfileNotificationHelper clipboardNotificationHelper,
        LocalClipboardSetter localClipboardSetter)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _configManager.ListenConfig<SyncConfig>(SyncConfigChanged);
        _syncConfig = _configManager.GetConfig<SyncConfig>();
        _configManager.ListenConfig<ServerConfig>(ServerConfigChanged);
        _serverConfig = _configManager.GetConfig<ServerConfig>();
        _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>();
        _notificationManager = _serviceProvider.GetRequiredService<INotificationManager>();
        _trayIcon = _serviceProvider.GetRequiredService<ITrayIcon>();
        _messenger = messenger;
        _uploadService = uploadService;
        _keyEventSimulator = keyEventSimulator;
        _hotkeyManager = hotkeyManager;
        _clipboardMoniter = clipboardMoniter;
        _clipboardListener = clipboardChangingListener;
        _remoteClipboardServerFactory = remoteClipboardServerFactory;
        _historyManager = _serviceProvider.GetRequiredService<HistoryManager>();
        _clipboardNotificationHelper = clipboardNotificationHelper;
        _localClipboardSetter = localClipboardSetter;

        _remoteClipboardServerFactory.CurrentServerChanged += OnCurrentServerChanged;
        _hotkeyManager.RegisterCommands(CommandCollection);
    }

    private void SyncConfigChanged(SyncConfig newConfig)
    {
        _syncConfig = newConfig;
        StopAndReload();
    }

    private void ServerConfigChanged(ServerConfig newConfig)
    {
        _serverConfig = newConfig;
        StopAndReload();
    }

    private void StopAndReload()
    {
        SwitchOffEventMode();
        ReLoad();
    }

    private void StopAndReloadByNewClipboard()
    {
        if (_downServiceChangingLocal)
            return;
        StopAndReload();
    }

    private void OnCurrentServerChanged(object? sender, EventArgs e)
    {
        _logger.Write(LOG_TAG, "Current server changed, restarting download service");
        StopAndReload();
    }

    private void ReLoad()
    {
        if (ClientSwitchOn)
            _trayIcon.SetActiveStatus(true);
        else
            _trayIcon.SetActiveStatus(false);

        if (SwitchOn)
            SwitchOnEventMode();
        else
            SwitchOffEventMode();
    }

    protected override void StartService()
    {
        ReLoad();
    }

    protected override void StopSerivce()
    {
        SwitchOffEventMode();
        _remoteClipboardServerFactory.CurrentServerChanged -= OnCurrentServerChanged;
    }

    private void SwitchOnEventMode()
    {
        lock (_serviceStateLocker)
        {
            if (!_isEventDrivenModeActive)
            {
                StartEventDrivenMode();

                _isEventDrivenModeActive = true;
                _clipboardMoniter.ClipboardChanged -= StopAndReloadByNewClipboard;
                _clipboardMoniter.ClipboardChanged += StopAndReloadByNewClipboard;
                _clipboardListener.Changed -= ClipboardProfileChanged;
                _clipboardListener.Changed += ClipboardProfileChanged;

                // 订阅远程剪贴板服务器的RemoteProfileChanged事件
                var remoteServer = _remoteClipboardServerFactory.Current;
                remoteServer.PollStatusEvent -= OnPollStatusChanged;
                remoteServer.PollStatusEvent += OnPollStatusChanged;

                remoteServer.RemoteProfileChanged -= OnRemoteProfileChanged;
                remoteServer.RemoteProfileChanged += OnRemoteProfileChanged;
            }
        }
    }

    private void SwitchOffEventMode()
    {
        lock (_serviceStateLocker)
        {
            if (_isEventDrivenModeActive)
            {
                _isEventDrivenModeActive = false;
                _clipboardMoniter.ClipboardChanged -= StopAndReloadByNewClipboard;
                _clipboardListener.Changed -= ClipboardProfileChanged;

                var remoteServer = _remoteClipboardServerFactory.Current;
                remoteServer.RemoteProfileChanged -= OnRemoteProfileChanged;
                remoteServer.PollStatusEvent -= OnPollStatusChanged;

                _localProfileCache = null;
                StopEventDrivenMode();
            }
        }
    }

    public void SetRemoteCache(Profile profile)
    {
        if (profile.Type != ProfileType.Unknown)
        {
            _remoteProfileCache = profile;
        }
    }

    private void StartEventDrivenMode()
    {
        _trayIcon.SetStatusString(SERVICE_NAME, "Running.", false);
        _logger.Write(LOG_TAG, "Event-driven mode started");
    }

    private void StopEventDrivenMode()
    {
        _trayIcon.SetStatusString(SERVICE_NAME, "Stopped.");
        _logger.Write(LOG_TAG, "Event-driven mode stopped");
    }

    private async void OnRemoteProfileChanged(object? sender, ProfileChangedEventArgs e)
    {
        var remoteProfile = e.NewProfile;
        _logger.Write(LOG_TAG, $"Remote profile changed: {remoteProfile}");

        try
        {
            await HandleRemoteProfileChange(remoteProfile);
        }
        catch (Exception ex)
        {
            _logger.Write(LOG_TAG, $"Error handling remote profile change: {ex.Message}");
        }
    }

    private async Task HandleRemoteProfileChange(Profile remoteProfile)
    {
        await _singleDownloadTask.Run(async (token) =>
        {
            if (!await NeedUpdate(remoteProfile, token))
            {
                return;
            }

            await SyncService.remoteProfilemutex.WaitAsync(token);
            using var remoteProfileMutexGuard = new ScopeGuard(() => SyncService.remoteProfilemutex.Release());

            await LocalClipboard.Semaphore.WaitAsync(token);
            using var localClipboardGuard = new ScopeGuard(() => LocalClipboard.Semaphore.Release());

            await DownloadRemoteProfile(remoteProfile, token);
        });
    }

    private void ClipboardProfileChanged(ClipboardMetaInfomation _, Profile profile)
    {
        _localProfileCache = profile;
    }

    private void OnPollStatusChanged(object? sender, PollStatusEventArgs e)
    {
        switch (e.Status)
        {
            case PollStatus.StoppedDueToNetworkIssues:
                _trayIcon.SetStatusString(SERVICE_NAME, "Unable to query remote clipboard.", true);
                break;

            case PollStatus.Resumed:
                _trayIcon.SetStatusString(SERVICE_NAME, "Running.", false);
                break;

            default:
                _logger.Write(LOG_TAG, $"Unknown poll status: {e.Status}");
                break;
        }
    }

    public override void RegistEventHandler()
    {
        Event.RegistEventHandler(SyncService.PUSH_START_ENENT_NAME, PushStartedHandler);
        Event.RegistEventHandler(SyncService.PUSH_STOP_ENENT_NAME, PushStoppedHandler);
    }

    public override void UnRegistEventHandler()
    {
        Event.UnRegistEventHandler(SyncService.PUSH_START_ENENT_NAME, PushStartedHandler);
        Event.UnRegistEventHandler(SyncService.PUSH_STOP_ENENT_NAME, PushStoppedHandler);
    }

    public void PushStartedHandler()
    {
        _logger.Write(LOG_TAG, "due to upload service start, cancel");
        SwitchOffEventMode();
    }

    public void PushStoppedHandler()
    {
        _logger.Write(LOG_TAG, "due to upload service stop, restart");
        StopAndReload();
    }

    private async Task<bool> NeedUpdate(Profile remoteProfile, CancellationToken token)
    {
        if (!_isQuickDownload && await Profile.Same(remoteProfile, _remoteProfileCache, token))
        {
            return false;
        }
        return true;
    }

    private async Task<Profile?> GetHistoryProfile(Profile remoteProfile, CancellationToken token)
    {
        var historyRecord = await _historyManager.GetHistoryRecord(await remoteProfile.GetHash(token), remoteProfile.Type, token);
        if (historyRecord is null || historyRecord.IsDeleted || !historyRecord.IsLocalFileReady)
            return null;

        try
        {
            var cachedProfile = historyRecord.ToProfile();
            var valid = await cachedProfile.IsLocalDataValid(false, token);
            if (!valid)
            {
                historyRecord.IsLocalFileReady = false;
                await _historyManager.UpdateHistoryLocalInfo(historyRecord, token);
                return null;
            }

            if (await Profile.Same(cachedProfile, remoteProfile, token))
            {
                return cachedProfile;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _historyManager.RemoveHistory(historyRecord, token);
            return null;
        }
        return null;
    }

    private async Task DownloadRemoteProfile(Profile profile, CancellationToken token)
    {
        _trayIcon.SetStatusString(SERVICE_NAME, "Downloading");
        _trayIcon.ShowDownloadAnimation();
        try
        {
            var currentLocalProfile = await _clipboardFactory.CreateProfileFromLocal(token);
            if (await Profile.Same(currentLocalProfile, profile, token))
            {
                _logger.Write(LOG_TAG, "Local clipboard is already same as remote profile, skipping download");
            }
            else
            {
                await DownloadAndSetRemoteProfileToLocal(profile, token);
                _remoteProfileCache = profile;
            }
            _trayIcon.SetStatusString(SERVICE_NAME, "Running.", false);
        }
        catch when (token.IsCancellationRequested)
        {
            _logger.Write(LOG_TAG, "Canceled");
            throw;
        }
        catch (RemoteServerException ex)
        {
            _logger.Write(LOG_TAG, $"Error downloading remote profile: {ex.Message}");
            _trayIcon.SetStatusString(SERVICE_NAME, $"Remote server Exception\n{ex.Message}", true);
            _notificationManager.ShowText(I18n.Strings.FailedToDownloadClipboard, ex.Message);
        }
        catch (Exception ex)
        {
            _nonServerErrorTimes++;
            _trayIcon.SetStatusString(SERVICE_NAME, $"Error. Failed times: {_nonServerErrorTimes}.\n{ex.Message}", true);
            _logger.Write(LOG_TAG, ex.Message);

            if (_nonServerErrorTimes > _syncConfig.RetryTimes)
            {
                _notificationManager.ShowText(I18n.Strings.FailedToDownloadClipboard, ex.Message);
                SwitchOffEventMode();
                _trayIcon.SetStatusString(SERVICE_NAME, $"Error for.\n{ex.Message}", true);
            }
        }
        finally
        {
            _toastReporter?.CancelSicent();
            _toastReporter = null;
            _trayIcon.StopAnimation();
            _downServiceChangingLocal = false;
            _messenger.Send(profile, SyncService.PULL_STOP_ENENT_NAME);
        }
    }

    private async Task DownloadAndSetRemoteProfileToLocal(Profile remoteProfile, CancellationToken cancelToken)
    {
        if (await Profile.Same(remoteProfile, _remoteProfileCache, cancelToken))
        {
            remoteProfile = _remoteProfileCache!;
        }
        else
        {
            var cachedProfile = await GetHistoryProfile(remoteProfile, cancelToken);
            if (cachedProfile is not null)
            {
                remoteProfile = cachedProfile;
                _logger.Write(SERVICE_NAME, $"Loaded from cache: {cachedProfile}");
            }
            else
            {
                await _historyManager.AddRemoteProfile(remoteProfile, cancelToken);
                await DownloadFileProfileData(remoteProfile, cancelToken);
                await _historyManager.AddLocalProfile(remoteProfile, token: cancelToken);
            }
        }
        _downServiceChangingLocal = true;
        _messenger.Send(EmptyMessage.Instance, SyncService.PULL_START_ENENT_NAME);

        if (!await IsLocalProfileObsolete(cancelToken))
        {
            await _localClipboardSetter.Set(remoteProfile, cancelToken, false);
            _localProfileCache = remoteProfile;
            _logger.Write(SERVICE_NAME, "Success set Local clipboard with remote profile: " + remoteProfile.ShortDisplayText);
            if (_syncConfig.NotifyOnDownloaded)
            {
                _clipboardNotificationHelper.Notify(remoteProfile, cancelToken);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancelToken);   // 设置本地剪贴板可能有延迟，延迟发送事件
        }
    }

    private async Task DownloadFileProfileData(Profile profile, CancellationToken cancelToken)
    {
        _logger.Write($"Downloading: {profile.ShortDisplayText}");
        _toastReporter = new ProgressToastReporter(profile.ShortDisplayText, I18n.Strings.DownloadingFile, _notificationManager);

        var remoteServer = _remoteClipboardServerFactory.Current;
        await remoteServer.DownloadProfileDataAsync(profile, _toastReporter, cancelToken);
    }

    private async Task<bool> IsLocalProfileObsolete(CancellationToken token)
    {
        if (_localProfileCache is null)
        {
            return false;
        }
        var profile = await _clipboardFactory.CreateProfileFromLocal(token);
        return !await Profile.Same(profile, _localProfileCache, token);
    }

    private void QuickDownload() => QuickDownload(false);

    private void QuickDownloadAndPaste()
    {
        if (_hotkeyManager.HotkeyStatusMap.TryGetValue(QuickDownloadAndPasteGuid, out var status))
        {
            status.Hotkey?.Keys.ForEach(key => _keyEventSimulator.SimulateKeyRelease(KeyCodeMap.MapReverse[key]));
        }

        QuickDownload(true);
    }

    private async void QuickDownload(bool paste)
    {
        _remoteProfileCache = null;
        _isQuickDownload = true;
        _isQuickDownloadAndPaste = paste;

        try
        {
            var remoteServer = _remoteClipboardServerFactory.Current;
            if (remoteServer != null)
            {
                var remoteProfile = await remoteServer.GetProfileAsync();
                await HandleRemoteProfileChange(remoteProfile);
                OnDownloadCompleted();
            }
        }
        catch (Exception ex)
        {
            _logger.Write(LOG_TAG, $"Quick download failed: {ex.Message}");
            _notificationManager.ShowText(I18n.Strings.FailedToDownloadClipboard, ex.Message);
        }
        finally
        {
            _isQuickDownload = false;
            _isQuickDownloadAndPaste = false;
        }
    }

    private void OnDownloadCompleted()
    {
        if (_isQuickDownloadAndPaste)
        {
            KeyCode modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;

            _keyEventSimulator.SimulateKeyPress(modifier);
            _keyEventSimulator.SimulateKeyPress(KeyCode.VcV);

            _keyEventSimulator.SimulateKeyRelease(KeyCode.VcV);
            _keyEventSimulator.SimulateKeyRelease(modifier);
        }
        _isQuickDownload = false;
        _isQuickDownloadAndPaste = false;
    }
}
