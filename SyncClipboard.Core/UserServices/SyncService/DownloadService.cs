using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Notification;

namespace SyncClipboard.Core.UserServices;

public class DownloadService : Service
{
    public event ProgramEvent.ProgramEventHandler? PullStarted;
    public event ProgramEvent.ProgramEventHandler? PullStopped;

    private const string SERVICE_NAME = "下载";
    private const string LOG_TAG = "PULL";
    private bool _isPullLoopRunning = false;
    private readonly object _isPullLoopRunningLocker = new();
    private ProgressToastReporter? _toastReporter;
    private Profile? _remoteProfileCache;

    private readonly NotificationManager _notificationManager;
    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly IClipboardFactory _clipboardFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITrayIcon _trayIcon;
    private SyncConfig _syncConfig;

    private bool SwitchOn => _syncConfig.SyncSwitchOn && _syncConfig.PullSwitchOn;

    public DownloadService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _configManager.ListenConfig<SyncConfig>(ConfigKey.Sync, SyncConfigChanged);
        _syncConfig = _configManager.GetConfig<SyncConfig>(ConfigKey.Sync) ?? new();
        _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>();
        _notificationManager = _serviceProvider.GetRequiredService<NotificationManager>();
        _trayIcon = _serviceProvider.GetRequiredService<ITrayIcon>();
    }

    private void SyncConfigChanged(object? newConfig)
    {
        _syncConfig = newConfig as SyncConfig ?? new();
        ReLoad();
    }

    private void ReLoad()
    {
        if (SwitchOn)
        {
            SwitchOnPullLoop();
        }
        else
        {
            SwitchOffPullLoop();
        }
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

    private void StartPullLoop()
    {
        _trayIcon.SetStatusString(SERVICE_NAME, "Running.");
        _cancelSource = new CancellationTokenSource();
        try
        {
            _ = PullLoop(_cancelSource.Token);
        }
        catch (OperationCanceledException)
        {
            _toastReporter?.CancelSicent();
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

    public override void RegistEvent()
    {
        var pullStartedEvent = new ProgramEvent(
            (handler) => PullStarted += handler,
            (handler) => PullStarted -= handler
        );
        Event.RegistEvent(SyncService.PULL_START_ENENT_NAME, pullStartedEvent);

        var pullStoppedEvent = new ProgramEvent(
            (handler) => PullStopped += handler,
            (handler) => PullStopped -= handler
        );
        Event.RegistEvent(SyncService.PULL_STOP_ENENT_NAME, pullStoppedEvent);
    }

    private void SetStatusOnError(ref int errorTimes, Exception ex)
    {
        errorTimes++;
        _trayIcon.SetStatusString(SERVICE_NAME, $"Error. Failed times: {errorTimes}.\n{ex.Message}", true);

        _logger.Write(ex.ToString());
        if (errorTimes == _syncConfig.RetryTimes)
        {
            _notificationManager.SendText("剪切板下载失败", ex.Message);
        }
    }

    private async Task PullLoop(CancellationToken cancelToken)
    {
        int errorTimes = 0;
        while (!cancelToken.IsCancellationRequested)
        {
            try
            {
                SyncService.remoteProfilemutex.WaitOne();
                var remoteProfile = await _clipboardFactory.CreateProfileFromRemote(cancelToken).ConfigureAwait(true);
                _logger.Write(LOG_TAG, "remote is " + remoteProfile.ToJsonString());

                if (await NeedUpdate(remoteProfile, cancelToken))
                {
                    await SetRemoteProfileToLocal(remoteProfile, cancelToken).ConfigureAwait(true);
                    _remoteProfileCache = remoteProfile;
                }
                _trayIcon.SetStatusString(SERVICE_NAME, "Running.", false);
                errorTimes = 0;
            }
            catch (TaskCanceledException)
            {
                cancelToken.ThrowIfCancellationRequested();
                _toastReporter?.Cancel();
                SetStatusOnError(ref errorTimes, new Exception("请求超时"));
            }
            catch (Exception ex)
            {
                SetStatusOnError(ref errorTimes, ex);
                _toastReporter?.Cancel();
            }
            finally
            {
                SyncService.remoteProfilemutex.ReleaseMutex();
            }

            await Task.Delay(TimeSpan.FromSeconds(_syncConfig.IntervalTime), cancelToken).ConfigureAwait(true);
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
        Profile localProfile = _clipboardFactory.CreateProfile();
        if (localProfile.Type == ProfileType.Unknown)
        {
            _logger.Write("[PULL] Local profile type is Unkown, stop sync.");
            return;
        }

        if (!await Profile.Same(remoteProfile, localProfile, cancelToken))
        {
            _trayIcon.SetStatusString(SERVICE_NAME, "Downloading");
            _trayIcon.ShowDownloadAnimation();
            try
            {
                if (remoteProfile is FileProfile)
                {
                    _toastReporter = new ProgressToastReporter(remoteProfile.FileName, "正在下载远程文件", _notificationManager);
                }
                await remoteProfile.BeforeSetLocal(cancelToken, _toastReporter);
                _toastReporter = null;
                PullStarted?.Invoke();
                var notifyer = _syncConfig.NotifyOnDownloaded ? _notificationManager : null;
                remoteProfile.SetLocalClipboard(notifyer);
                _logger.Write("剪切板同步成功:" + remoteProfile.Text);
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancelToken);   // 设置本地剪切板可能有延迟，延迟发送事件
            }
            catch
            {
                throw;
            }
            finally
            {
                _trayIcon.StopAnimation();
                PullStopped?.Invoke();
            }
        }
    }
}
