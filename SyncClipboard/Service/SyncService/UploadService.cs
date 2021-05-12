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
                    PushStopped?.Invoke();
                    return;
                }
                PushStarted?.Invoke();
                _uploadQueue = 0;
            }

            await UploadClipboard().ConfigureAwait(false);

            lock (_uploaderWorkingLocker)
            {
                _isUploaderWorking = false;
            }
            ProcessUploadQueue();
        }

        private async Task UploadClipboard()
        {
            await Task.Delay(2000).ConfigureAwait(false);
            Log.Write("STARTTTTT");
            var currentProfile = ProfileFactory.CreateFromLocal();
            if (currentProfile == null || currentProfile.GetProfileType() == ProfileType.ClipboardType.Unknown)
            {
                Log.Write("Local profile type is Unkown, stop upload.");
                return;
            }

            Log.Write("start loop");
            lock (SyncService.remoteProfileLocker)
            {
                string errMessage = "";
                for (int i = 0; i < UserConfig.Config.Program.RetryTimes; i++)
                {
                    try
                    {
                        currentProfile.UploadProfile(Program.webDav);
                        Log.Write("[PUSH] upload end");
                        return;
                    }
                    catch (System.Exception ex)
                    {
                        errMessage = ex.Message;
                        //Program.notifyer.SetStatusString(SERVICE_NAME, $"失败，正在第{i + 1}次尝试，错误原因：{errMessage}", _isErrorStatus);
                    }

                    //Thread.Sleep(UserConfig.Config.Program.IntervalTime);
                }
                Program.notifyer.ToastNotify("上传失败：" + currentProfile.ToolTip(), errMessage);
                //_statusString = errMessage;
                //_isErrorStatus = true;
            }
        }
    }
}