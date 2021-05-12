using SyncClipboard.Module;

namespace SyncClipboard.Service
{
    public class UploadService : Service
    {
        public const string PUSH_START_ENENT_NAME = "PUSH_START_ENENT";
        public const string PUSH_STOP_ENENT_NAME = "PUSH_STOP_ENENT";
        public event Event.ProgramEvent PushStarted;
        public event Event.ProgramEvent PushStopped;

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
            Event.RegistEvent(PUSH_START_ENENT_NAME, PushStarted);
            Event.RegistEvent(PUSH_START_ENENT_NAME, PushStopped);
        }

        public override void RegistEventHandler()
        {
            base.RegistEventHandler();
        }

        public override void UnRegistEventHandler()
        {
            base.UnRegistEventHandler();
        }

    }
}