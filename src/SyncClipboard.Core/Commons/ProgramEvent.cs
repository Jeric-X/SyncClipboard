namespace SyncClipboard.Core.Commons
{
    public class ProgramEvent(Action<ProgramEvent.ProgramEventHandler> addAction, Action<ProgramEvent.ProgramEventHandler> removeAction)
    {
        public delegate void ProgramEventHandler();

        public void Add(ProgramEventHandler handler)
        {
            addAction(handler);
        }

        public void Remove(ProgramEventHandler handler)
        {
            removeAction(handler);
        }
    }
}
