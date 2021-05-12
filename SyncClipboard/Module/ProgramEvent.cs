using System;

namespace SyncClipboard.Module
{
    public class ProgramEvent
    {
        public delegate void ProgramEventHandler();

        private readonly Action<ProgramEventHandler> _addAction;
        private readonly Action<ProgramEventHandler> _removeAction;

        public ProgramEvent(Action<ProgramEventHandler> addAction, Action<ProgramEventHandler> removeAction)
        {
            _addAction = addAction;
            _removeAction = removeAction;
        }

        public void Add(ProgramEventHandler handler)
        {
            _addAction(handler);
        }

        public void Remove(ProgramEventHandler handler)
        {
            _removeAction(handler);
        }
    }
}
