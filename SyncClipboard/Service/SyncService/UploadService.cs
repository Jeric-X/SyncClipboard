using System.Threading.Tasks;
using SyncClipboard.Module;
using SyncClipboard.Utility;

namespace SyncClipboard.Service
{
    public class UploadService : Service
    {
        public event ProgramEvent.ProgramEventHandler PushStarted;
        public event ProgramEvent.ProgramEventHandler PushStopped;

        private const string SERVICE_NAME = "⬆⬆";
        private bool _isChangingLocal = false;

        protected override void StartService()
        {
            Global.Notifyer.SetStatusString(SERVICE_NAME, "Running.");
        }

        protected override void StopSerivce()
        {
            Global.Notifyer.SetStatusString(SERVICE_NAME, "Stopped.");
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
            Event.RegistEventHandler(ClipboardService.CLIPBOARD_CHANGED_EVENT_NAME, ClipBoardChangedHandler);
            Event.RegistEventHandler(SyncService.PULL_START_ENENT_NAME, PullStartedHandler);
            Event.RegistEventHandler(SyncService.PULL_STOP_ENENT_NAME, PullStoppedHandler);
        }

        public override void UnRegistEventHandler()
        {
            Event.UnRegistEventHandler(ClipboardService.CLIPBOARD_CHANGED_EVENT_NAME, ClipBoardChangedHandler);
            Event.UnRegistEventHandler(SyncService.PULL_START_ENENT_NAME, PullStartedHandler);
            Event.UnRegistEventHandler(SyncService.PULL_STOP_ENENT_NAME, PullStoppedHandler);
        }

        public void PullStartedHandler()
        {
            Log.Write("_isChangingLocal set to TRUE");
            _isChangingLocal = true;
        }

        public void PullStoppedHandler()
        {
            Log.Write("_isChangingLocal set to FALSE");
            _isChangingLocal = false;
        }

        private int _uploadQueue = 0;
        private readonly object _uploadQueueLocker = new object();

        private bool _isUploaderWorking = false;
        private readonly object _uploaderWorkingLocker = new object();

        private void ClipBoardChangedHandler()
        {
            if (!UserConfig.Config.SyncService.PushSwitchOn || _isChangingLocal)
            {
                return;
            }

            lock(_uploadQueueLocker)
            {
                _uploadQueue++;
            }

            ProcessUploadQueue();
        }

        private async void ProcessUploadQueue()
        {
            lock (_uploaderWorkingLocker)
            {
                if (_isUploaderWorking)
                {
                    return;
                }
                _isUploaderWorking = true;
            }

            lock (_uploadQueueLocker)
            {
                if (_uploadQueue == 0)
                {
                    StopUploadingIcon();
                    PushStopped?.Invoke();
                    _isUploaderWorking = false;
                    return;
                }
                Global.Notifyer.SetStatusString(SERVICE_NAME, "Uploading.");
                SetUploadingIcon();
                PushStarted?.Invoke();
                _uploadQueue = 0;
            }

            await UploadClipboard().ConfigureAwait(true);

            lock (_uploaderWorkingLocker)
            {
                _isUploaderWorking = false;
            }
            ProcessUploadQueue();
        }

        private async Task UploadClipboard()
        {
            var currentProfile = ProfileFactory.CreateFromLocal();
            if (currentProfile == null)
            {
                Log.Write("Local profile type is null, stop upload.");
                return;
            }

            if (currentProfile == null || currentProfile.GetProfileType() == ProfileType.ClipboardType.Unknown)
            {
                Log.Write("Local profile type is Unkown, stop upload.");
                return;
            }

            await UploadLoop(currentProfile).ConfigureAwait(true);
        }

        private async Task UploadLoop(Profile profile)
        {
            SyncService.remoteProfilemutex.WaitOne();

            string errMessage = "";
            for (int i = 0; i < UserConfig.Config.Program.RetryTimes; i++)
            {
                try
                {
                    await profile.UploadProfileAsync(Global.WebDav).ConfigureAwait(true);
                    Log.Write("Upload end");
                    Global.Notifyer.SetStatusString(SERVICE_NAME, "Running.", false);
                    SyncService.remoteProfilemutex.ReleaseMutex();
                    return;
                }
                catch (System.Exception ex)
                {
                    errMessage = ex.Message;
                    Global.Notifyer.SetStatusString(SERVICE_NAME, $"失败，正在第{i + 1}次尝试，错误原因：{errMessage}", true);
                }

                await Task.Delay(UserConfig.Config.Program.IntervalTime).ConfigureAwait(true);
            }
            SyncService.remoteProfilemutex.ReleaseMutex();
            Global.Notifyer.ToastNotify("上传失败：" + profile.ToolTip(), errMessage);
        }

        private void SetUploadingIcon()
        {
            System.Drawing.Icon[] icon =
            {
                Properties.Resources.upload001, Properties.Resources.upload002, Properties.Resources.upload003,
                Properties.Resources.upload004, Properties.Resources.upload005, Properties.Resources.upload006,
                Properties.Resources.upload007, Properties.Resources.upload008, Properties.Resources.upload009,
                Properties.Resources.upload010, Properties.Resources.upload011, Properties.Resources.upload012,
                Properties.Resources.upload013, Properties.Resources.upload014, Properties.Resources.upload015,
                Properties.Resources.upload016, Properties.Resources.upload017,
            };

            Global.Notifyer.SetDynamicNotifyIcon(icon, 150);
        }

        private void StopUploadingIcon()
        {
            Global.Notifyer.StopDynamicNotifyIcon();
        }
    }
}