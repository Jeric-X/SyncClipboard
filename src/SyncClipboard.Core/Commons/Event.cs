namespace SyncClipboard.Core.Commons
{
    public static class Event
    {
        private static readonly Dictionary<string, ProgramEvent> _savedEvent = [];

        public static bool RegistEvent(string eventName, ProgramEvent programEvent)
        {
            if (_savedEvent.ContainsKey(eventName))
            {
                return false;
            }
            _savedEvent.Add(eventName, programEvent);
            return true;
        }

        public static bool UnRegistEvent(string eventName)
        {
            if (!_savedEvent.ContainsKey(eventName))
            {
                return false;
            }
            _savedEvent.Remove(eventName);
            return true;
        }

        public static bool RegistEventHandler(string eventName, ProgramEvent.ProgramEventHandler eventhandler)
        {
            if (_savedEvent.TryGetValue(eventName, out ProgramEvent? programEvent))
            {
                programEvent.Add(eventhandler);
                return true;
            }

            return false;
        }

        public static bool UnRegistEventHandler(string eventName, ProgramEvent.ProgramEventHandler eventhandler)
        {
            if (_savedEvent.TryGetValue(eventName, out ProgramEvent? value))
            {
                value.Remove(eventhandler);
                return true;
            }

            return false;
        }
    }
}