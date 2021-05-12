using System.Collections.Generic;

namespace SyncClipboard.Module
{
    public static class Event
    {
        public delegate void ProgramEvent();

        private static readonly Dictionary<string, ProgramEvent> _savedEvent = new Dictionary<string, ProgramEvent>();

        public static bool RegistEvent(string eventName, ProgramEvent eventDelegate)
        {
            if (_savedEvent.ContainsKey(eventName))
            {
                return false;
            }
            _savedEvent.Add(eventName, eventDelegate);
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

        public static bool RegistEventHandler(string eventName, ProgramEvent eventDelegate)
        {
            if (_savedEvent.ContainsKey(eventName))
            {
                _savedEvent[eventName] += eventDelegate;
                return true;
            }

            return false;
        }

        public static bool UnRegistEventHandler(string eventName, ProgramEvent eventDelegate)
        {
            if (_savedEvent.ContainsKey(eventName))
            {
                _savedEvent[eventName] -= eventDelegate;
                return true;
            }

            return false;
        }
    }
}