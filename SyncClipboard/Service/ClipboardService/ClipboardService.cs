using SyncClipboard.Core.Commons;

namespace SyncClipboard.Service
{
    public class ClipboardService : Core.Interfaces.Service
    {
        public const string CLIPBOARD_CHANGED_EVENT_NAME = "CLIPBOARD_CHANGED_EVENT";
        public event ProgramEvent.ProgramEventHandler ClipBoardChanged;
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
            var programEvent = new ProgramEvent(
                (handler) => ClipBoardChanged += handler,
                (handler) => ClipBoardChanged -= handler
            );
            Event.RegistEvent(CLIPBOARD_CHANGED_EVENT_NAME, programEvent);
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