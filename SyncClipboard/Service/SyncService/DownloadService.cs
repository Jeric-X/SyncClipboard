using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Notification;
using SyncClipboard.Core.Clipboard;
using System;
using System.Threading;
using System.Threading.Tasks;
#nullable enable

namespace SyncClipboard.Service
{
    public class DownloadService : Core.Interfaces.Service
    {
        public event ProgramEvent.ProgramEventHandler? PullStarted;
        public event ProgramEvent.ProgramEventHandler? PullStopped;

        private const string SERVICE_NAME = "⬇⬇";
        private const string LOG_TAG = "PULL";
        private bool _pullSwitchOn = false;
        private readonly object _pullSwitchLocker = new();
        private ProgressToastReporter? _toastReporter;
        private Profile? _remoteProfileCache;

        private readonly NotificationManager _notificationManager;
        private readonly ILogger _logger;
        private readonly UserConfig _userConfig;
        private readonly IClipboardFactory _clipboardFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly ITrayIcon _trayIcon;

        public DownloadService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _logger = _serviceProvider.GetRequiredService<ILogger>();
            _userConfig = _serviceProvider.GetRequiredService<UserConfig>();
            _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>();
            _notificationManager = _serviceProvider.GetRequiredService<NotificationManager>();
            _trayIcon = _serviceProvider.GetRequiredService<ITrayIcon>();
        }

        public override void Load()
        {
            if (_userConfig.Config.SyncService.PullSwitchOn)
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
            var ToggleMenuItem = new ToggleMenuItem("下载远程", _userConfig.Config.SyncService.PullSwitchOn, (status) =>
            {
                _userConfig.Config.SyncService.PullSwitchOn = status;
                _userConfig.Save();
            });
            _serviceProvider.GetRequiredService<IContextMenu>().AddMenuItem(ToggleMenuItem);
            Load();
        }

        protected override void StopSerivce()
        {
            SwitchOffPullLoop();
        }

        private void SwitchOnPullLoop()
        {
            lock (_pullSwitchLocker)
            {
                if (!_pullSwitchOn)
                {
                    _pullSwitchOn = true;
                    StartPullLoop();
                }
            }
        }

        private void SwitchOffPullLoop()
        {
            lock (_pullSwitchLocker)
            {
                if (_pullSwitchOn)
                {
                    _pullSwitchOn = false;
                    StopPullLoop();
                }
            }
        }

        private void StartPullLoop()
        {
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
            StopPullLoop();
        }

        public void PushStoppedHandler()
        {
            _logger.Write(LOG_TAG, "due to upload service stop, cancel");
            StopPullLoop();
            if (_userConfig.Config.SyncService.PullSwitchOn)
            {
                StartPullLoop();
            }
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
            _trayIcon.SetStatusString(SERVICE_NAME, $"Error. Failed times: {errorTimes}.", true);

            _logger.Write(ex.ToString());
            if (errorTimes == _userConfig.Config.Program.RetryTimes)
            {
                _notificationManager.SendText("剪切板下载失败", ex.Message);
            }
        }

        private async Task PullLoop(CancellationToken cancelToken)
        {
            int errorTimes = 0;
            while (!cancelToken.IsCancellationRequested)
            {
                _trayIcon.SetStatusString(SERVICE_NAME, "Reading remote profile.");

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

                await Task.Delay(TimeSpan.FromSeconds(_userConfig.Config.Program.IntervalTime), cancelToken).ConfigureAwait(true);
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
                    remoteProfile.SetLocalClipboard(_notificationManager);
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
}
