using System;
using System.Threading;
using SyncClipboard.Utility;
using SyncClipboard.Module;
using System.Threading.Tasks;

namespace SyncClipboard.Service
{
    public class DownloadService : Service
    {
        public event ProgramEvent.ProgramEventHandler PullStarted;
        public event ProgramEvent.ProgramEventHandler PullStopped;

        private const string SERVICE_NAME = "⬇⬇";
        private bool _isChangingRemote = false;

        public override void Load()
        {
            if (UserConfig.Config.SyncService.PullSwitchOn)
            {
                this.StartService();
            }
            else
            {
                this.StopSerivce();
            }
        }

        private CancellationTokenSource _cancelSource;
        private CancellationToken _cancelToken;

        protected override void StartService()
        {
            if (UserConfig.Config.SyncService.PullSwitchOn)
            {
                _cancelSource = new CancellationTokenSource();
                _cancelToken = _cancelSource.Token;
                StartServiceAsync();
            }
        }

        protected async void StartServiceAsync()
        {
            await PullLoop().ConfigureAwait(true);
        }

        protected override void StopSerivce()
        {
            _cancelSource.Cancel();
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
            Log.Write("_isChangingRemote set to TRUE");
            _isChangingRemote = true;
        }

        public void PushStoppedHandler()
        {
            Log.Write("_isChangingRemote set to FALSE");
            _isChangingRemote = false;
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

        private async Task PullLoop()
        {
            int errorTimes = 0;
            while (!_cancelToken.IsCancellationRequested)
            {
                Global.Notifyer.SetStatusString(SERVICE_NAME, "Reading remote profile.");
                SyncService.remoteProfilemutex.WaitOne();

                Profile remoteProfile = null;
                try
                {
                    remoteProfile = await ProfileFactory.CreateFromRemote(Global.WebDav).ConfigureAwait(true);
                    await SetRemoteProfileToLocal(remoteProfile).ConfigureAwait(true);
                    Global.Notifyer.SetStatusString(SERVICE_NAME, "Running.", false);
                    errorTimes = 0;
                }
                catch (Exception ex)
                {
                    errorTimes++;
                    Global.Notifyer.SetStatusString(SERVICE_NAME, $"Error. Failed times: {errorTimes}.", true);

                    Log.Write(ex.ToString());
                    if (errorTimes == UserConfig.Config.Program.RetryTimes)
                    {
                        Global.Notifyer.ToastNotify("剪切板同步失败", ex.Message);
                    }
                }
                finally
                {
                    SyncService.remoteProfilemutex.ReleaseMutex();
                }

                await Task.Delay(UserConfig.Config.Program.IntervalTime).ConfigureAwait(true);
            }
        }

        private async Task SetRemoteProfileToLocal(Profile remoteProfile)
        {
            Profile localProfile = ProfileFactory.CreateFromLocal();
            if (localProfile.GetProfileType() == ProfileType.ClipboardType.Unknown)
            {
                Log.Write("[PULL] Local profile type is Unkown, stop sync.");
                return;
            }

            Log.Write("[PULL] isChangingRemote = " + _isChangingRemote.ToString());
            if (!_isChangingRemote && remoteProfile != localProfile)
            {
                Thread.Sleep(200);
                if (!_isChangingRemote)
                {
                    SetDownloadingIcon();
                    PullStarted?.Invoke();

                    await remoteProfile.SetLocalClipboard().ConfigureAwait(true);

                    Log.Write("剪切板同步成功:" + remoteProfile.Text);
                    Global.Notifyer.ToastNotify("剪切板同步成功", remoteProfile.ToolTip(), remoteProfile.ExecuteProfile());
                    StopDownloadingIcon();

                    Thread.Sleep(50);
                    PullStopped?.Invoke();
                }
            }
        }

        private void SetDownloadingIcon()
        {
            System.Drawing.Icon[] icon =
            {
                Properties.Resources.download001, Properties.Resources.download002, Properties.Resources.download003,
                Properties.Resources.download004, Properties.Resources.download005, Properties.Resources.download006,
                Properties.Resources.download007, Properties.Resources.download008, Properties.Resources.download009,
                Properties.Resources.download010, Properties.Resources.download011, Properties.Resources.download012,
                Properties.Resources.download013, Properties.Resources.download014, Properties.Resources.download015,
                Properties.Resources.download016, Properties.Resources.download017,
            };

            Global.Notifyer.SetDynamicNotifyIcon(icon, 150);
        }

        private void StopDownloadingIcon()
        {
            Global.Notifyer.StopDynamicNotifyIcon();
        }
    }
}
