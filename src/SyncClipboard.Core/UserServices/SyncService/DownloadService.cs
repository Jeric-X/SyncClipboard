using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Core.UserServices;

public class DownloadService : Service
{
    private readonly static string SERVICE_NAME = I18n.Strings.DownloadService;
    private const string LOG_TAG = "PULL";
    private bool _isPullLoopRunning = false;
    private bool _isQuickDownload = false;
    private readonly object _isPullLoopRunningLocker = new();
    private ProgressToastReporter? _toastReporter;
    private Profile? _remoteProfileCache;

    private readonly INotification _notificationManager;
    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly IClipboardFactory _clipboardFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITrayIcon _trayIcon;
    private readonly IMessenger _messenger;
    private readonly UploadService _uploadService;
    private SyncConfig _syncConfig;
    private ServerConfig _serverConfig;

    private bool SwitchOn => _syncConfig.SyncSwitchOn && _syncConfig.PullSwitchOn && (!_serverConfig.ClientMixedMode || !_serverConfig.SwitchOn);
    private bool ClientSwitchOn => _syncConfig.SyncSwitchOn || (_serverConfig.ClientMixedMode && _serverConfig.SwitchOn);

    #region Hotkey
    private UniqueCommandCollection CommandCollection => new(PageDefinition.SyncSetting.Title, PageDefinition.SyncSetting.FontIcon!)
    {
        Commands = {
            new UniqueCommand(
                I18n.Strings.SwitchClipboardSyncing,
                Guid.Parse("26D8A39E-F50D-CC71-FE15-647F67FDB2F9"),
                () => SwitchClipboardSyncing(!_syncConfig.SyncSwitchOn)
            ),
            new UniqueCommand(
                I18n.Strings.SwitchBuiltInServer,
                Guid.Parse("145740F4-03F7-6F6C-5B93-B027C7C49C59"),
                () => SwitchBuiltInServer(!_serverConfig.SwitchOn)
            ),
            new UniqueCommand(
                I18n.Strings.SwitchMixedClientMode,
                Guid.Parse("1D5C8163-E2E0-D099-A334-62A4B4F2BCE5"),
                () => SwitchMixedClientMode(!_serverConfig.ClientMixedMode)
            ),
            _uploadService.QuickUploadCommand,
            new UniqueCommand(
                I18n.Strings.DownloadOnce,
                Guid.Parse("95396FFF-E5FE-45D3-9D70-4A43FA34FF31"),
                QuickDownload
            ),
        }
    };

    private void SwitchClipboardSyncing(bool isOn)
    {
        _configManager.SetConfig(_syncConfig with { SyncSwitchOn = isOn });
        var para = new NotificationPara
        {
            Duration = TimeSpan.FromSeconds(2),
            Title = isOn ? I18n.Strings.SwitchOnClipboardSyncing : I18n.Strings.SwitchOffClipboardSyncing,
        };
        _notificationManager.Send(para);
    }

    private void SwitchBuiltInServer(bool isOn)
    {
        _configManager.SetConfig(_serverConfig with { SwitchOn = isOn });
        var para = new NotificationPara
        {
            Duration = TimeSpan.FromSeconds(2),
            Title = isOn ? I18n.Strings.SwitchOnBuiltInServer : I18n.Strings.SwitchOffBuiltInServer
        };
        _notificationManager.Send(para);
    }

    private void SwitchMixedClientMode(bool isOn)
    {
        _configManager.SetConfig(_serverConfig with { ClientMixedMode = isOn });
        var para = new NotificationPara
        {
            Duration = TimeSpan.FromSeconds(2),
            Title = isOn ? I18n.Strings.SwitchOnMixedClientMode : I18n.Strings.SwitchOffMixedClientMode
        };
        _notificationManager.Send(para);
    }
    #endregion

    public DownloadService(IServiceProvider serviceProvider, IMessenger messenger, UploadService uploadService)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _configManager.ListenConfig<SyncConfig>(SyncConfigChanged);
        _syncConfig = _configManager.GetConfig<SyncConfig>();
        _configManager.ListenConfig<ServerConfig>(ServerConfigChanged);
        _serverConfig = _configManager.GetConfig<ServerConfig>();
        _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>();
        _notificationManager = _serviceProvider.GetRequiredService<INotification>();
        _trayIcon = _serviceProvider.GetRequiredService<ITrayIcon>();
        _messenger = messenger;
        _uploadService = uploadService;

        serviceProvider.GetService<HotkeyManager>()?.RegisterCommands(CommandCollection);
    }

    private void SyncConfigChanged(SyncConfig newConfig)
    {
        _syncConfig = newConfig;
        ReLoad();
    }

    private void ServerConfigChanged(ServerConfig newConfig)
    {
        _serverConfig = newConfig;
        ReLoad();
    }

    private void ReLoad()
    {
        if (ClientSwitchOn)
            _trayIcon.SetActiveStatus(true);
        else
            _trayIcon.SetActiveStatus(false);

        if (SwitchOn)
            SwitchOnPullLoop();
        else
            SwitchOffPullLoop();
    }

    private CancellationTokenSource? _cancelSource;

    protected override void StartService()
    {
        ReLoad();
    }

    protected override void StopSerivce()
    {
        SwitchOffPullLoop();
    }

    private void SwitchOnPullLoop()
    {
        lock (_isPullLoopRunningLocker)
        {
            if (!_isPullLoopRunning)
            {
                _isPullLoopRunning = true;
                StartPullLoop();
            }
        }
    }

    private void SwitchOffPullLoop()
    {
        lock (_isPullLoopRunningLocker)
        {
            if (_isPullLoopRunning)
            {
                _isPullLoopRunning = false;
                StopPullLoop();
            }
        }
    }

    private async void StartPullLoop()
    {
        _trayIcon.SetStatusString(SERVICE_NAME, "Running.");
        _cancelSource = new CancellationTokenSource();
        try
        {
            await PullLoop(_cancelSource.Token);
        }
        catch (OperationCanceledException)
        {
            _toastReporter?.CancelSicent();
            _toastReporter = null;
            _logger.Write(LOG_TAG, "Canceled");
        }
    }

    private void StopPullLoop()
    {
        _trayIcon.SetStatusString(SERVICE_NAME, "Stopped.");
        _cancelSource?.Cancel();
        _cancelSource = null;
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
        SwitchOffPullLoop();
    }

    public void PushStoppedHandler()
    {
        _logger.Write(LOG_TAG, "due to upload service stop, restart");
        SwitchOffPullLoop();
        ReLoad();
    }

    private void SetStatusOnError(ref int errorTimes, Exception ex)
    {
        errorTimes++;
        if (errorTimes == 2)
        {
            _trayIcon.SetStatusString(SERVICE_NAME, $"Error. Failed times: {errorTimes}.\n{ex.Message}", true);
        }

        _logger.Write(ex.ToString());
        if (errorTimes == _syncConfig.RetryTimes)
        {
            _notificationManager.SendText(I18n.Strings.FailedToDownloadClipboard, ex.Message);
        }
    }

    private async Task PullLoop(CancellationToken cancelToken)
    {
        int errorTimes = 0;
        while (!cancelToken.IsCancellationRequested)
        {
            await SyncService.remoteProfilemutex.WaitAsync(cancelToken);
            try
            {
                var remoteProfile = await _clipboardFactory.CreateProfileFromRemote(cancelToken);
                _logger.Write(LOG_TAG, "remote is " + remoteProfile.ToJsonString());

                if (await NeedUpdate(remoteProfile, cancelToken))
                {
                    await SetRemoteProfileToLocal(remoteProfile, cancelToken);
                    _remoteProfileCache = remoteProfile;
                }
                _trayIcon.SetStatusString(SERVICE_NAME, "Running.", false);
                errorTimes = 0;
                if (_isQuickDownload && !SwitchOn)
                {
                    _isQuickDownload = false;
                    return;
                }
            }
            catch (TaskCanceledException)
            {
                cancelToken.ThrowIfCancellationRequested();
                _toastReporter?.Cancel();
                _toastReporter = null;
                SetStatusOnError(ref errorTimes, new Exception("Request timeout"));
            }
            catch (Exception ex)
            {
                SetStatusOnError(ref errorTimes, ex);
                _toastReporter?.Cancel();
                _toastReporter = null;
            }
            finally
            {
                SyncService.remoteProfilemutex.Release();
            }

            await Task.Delay(TimeSpan.FromSeconds(_syncConfig.IntervalTime), cancelToken);
        }
    }

    private async Task<bool> NeedUpdate(Profile remoteProfile, CancellationToken cancelToken)
    {
        if (await Profile.Same(remoteProfile, _remoteProfileCache, cancelToken))
        {
            return false;
        }

        if (remoteProfile is FileProfile)
        {
            if (await (remoteProfile as FileProfile)!.Oversized(cancelToken))
            {
                return false;
            }
        }
        return true;
    }

    private async Task SetRemoteProfileToLocal(Profile remoteProfile, CancellationToken cancelToken)
    {
        var meta = await _clipboardFactory.GetMetaInfomation(cancelToken);
        Profile localProfile = _clipboardFactory.CreateProfileFromMeta(meta);

        if (!await Profile.Same(remoteProfile, localProfile, cancelToken))
        {
            _trayIcon.SetStatusString(SERVICE_NAME, "Downloading");
            _trayIcon.ShowDownloadAnimation();
            try
            {
                if (remoteProfile is FileProfile)
                {
                    _toastReporter = new ProgressToastReporter(remoteProfile.FileName, I18n.Strings.DownloadingFile, _notificationManager);
                }
                await remoteProfile.BeforeSetLocal(cancelToken, _toastReporter);
                _messenger.Send(EmptyMessage.Instance, SyncService.PULL_START_ENENT_NAME);
                remoteProfile.SetLocalClipboard(true, cancelToken);
                _logger.Write("Success download:" + remoteProfile.Text);
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancelToken);   // 设置本地剪贴板可能有延迟，延迟发送事件
            }
            finally
            {
                _trayIcon.StopAnimation();
                _messenger.Send(remoteProfile, SyncService.PULL_STOP_ENENT_NAME);
            }
        }
    }

    private void QuickDownload()
    {
        _isQuickDownload = true;
        SwitchOffPullLoop();
        SwitchOnPullLoop();
    }
}
