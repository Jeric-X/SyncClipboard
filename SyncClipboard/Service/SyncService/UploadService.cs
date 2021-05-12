using System.Threading.Tasks;
using SyncClipboard.Module;
using SyncClipboard.Utility;

namespace SyncClipboard.Service
{
    public class UploadService : Service
    {
        public const string PUSH_START_ENENT_NAME = "PUSH_START_ENENT";
        public const string PUSH_STOP_ENENT_NAME = "PUSH_STOP_ENENT";
        public event ProgramEvent.ProgramEventHandler PushStarted;
        public event ProgramEvent.ProgramEventHandler PushStopped;

        private const string SERVICE_NAME = "⬆⬆";

        protected override void StartService()
        {
            Program.notifyer.SetStatusString(SERVICE_NAME, "Running.");
        }

        protected override void StopSerivce()
        {
            Program.notifyer.SetStatusString(SERVICE_NAME, "Stopped.");
        }

        public override void Load()
        {
            if (UserConfig.Config.SyncService.PushSwitchOn)
            {
                this.Start();
            }
            else
            {
                this.Stop();
            }
        }

        public override void RegistEvent()
        {
            var pushStartedEvent = new ProgramEvent(
                (handler) => PushStarted += handler,
                (handler) => PushStarted -= handler
            );
            Event.RegistEvent(PUSH_START_ENENT_NAME, pushStartedEvent);

            var pushStoppedEvent = new ProgramEvent(
                (handler) => PushStopped += handler,
                (handler) => PushStopped -= handler
            );
            Event.RegistEvent(PUSH_START_ENENT_NAME, pushStoppedEvent);
        }

        public override void RegistEventHandler()
        {
            Event.RegistEventHandler(ClipboardService.CLIPBOARD_CHANGED_EVENT_NAME, ClipBoardChangedHandler);
        }

        public override void UnRegistEventHandler()
        {
            Event.UnRegistEventHandler(ClipboardService.CLIPBOARD_CHANGED_EVENT_NAME, ClipBoardChangedHandler);
        }

        private int _uploadQueue = 0;
        private readonly object _uploadQueueLocker = new object();

        private bool _isUploaderWorking = false;
        private readonly object _uploaderWorkingLocker = new object();

        private void ClipBoardChangedHandler()
        {
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
                Program.notifyer.SetStatusString(SERVICE_NAME, "Uploading.");
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

            SyncService.remoteProfilemutex.WaitOne();

            string errMessage = "";
            for (int i = 0; i < UserConfig.Config.Program.RetryTimes; i++)
            {
                try
                {
                    await currentProfile.UploadProfileAsync(Program.webDav).ConfigureAwait(true);
                    Log.Write("Upload end");
                    Program.notifyer.SetStatusString(SERVICE_NAME, "Running.", false);
                    SyncService.remoteProfilemutex.ReleaseMutex();
                    return;
                }
                catch (System.Exception ex)
                {
                    errMessage = ex.Message;
                    Program.notifyer.SetStatusString(SERVICE_NAME, $"失败，正在第{i + 1}次尝试，错误原因：{errMessage}", true);
                }

                await Task.Delay(UserConfig.Config.Program.IntervalTime).ConfigureAwait(true);
            }
            SyncService.remoteProfilemutex.ReleaseMutex();
            Program.notifyer.ToastNotify("上传失败：" + currentProfile.ToolTip(), errMessage);
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

            Program.notifyer.SetDynamicNotifyIcon(icon, 150);
        }

        private void StopUploadingIcon()
        {
            Program.notifyer.StopDynamicNotifyIcon();
        }
    }
}