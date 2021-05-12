using SyncClipboard.Module;

namespace SyncClipboard.Service
{
    public class ClipboardService : Service
    {
        public const string CLIPBOARD_CHANGED_EVENT_NAME = "CLIPBOARD_CHANGED_EVENT";
        public event Event.ProgramEvent ClipBoardChanged;
        private ClipboardListener _listener;

        protected override void StartService()
        {
            _listener = new ClipboardListener();
            _listener.AddHandler(ClipBoardChangedHandler);
        }

        protected override void StopSerivce()
        {
            _listener.RemoveHandler(ClipBoardChangedHandler);
            _listener.Dispose();
        }

        public override void RegistEvent()
        {
            Event.RegistEvent(CLIPBOARD_CHANGED_EVENT_NAME, ClipBoardChanged);
        }

        public override void UnRegistEvent()
        {
            Event.UnRegistEvent(CLIPBOARD_CHANGED_EVENT_NAME);
        }

        private void ClipBoardChangedHandler()
        {
            ClipBoardChanged?.Invoke();
        }
    }
}