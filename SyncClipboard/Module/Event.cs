using System.Collections.Generic;

namespace SyncClipboard.Module
{
    public static class Event
    {
        public delegate void SystemEvent();

        private static readonly Dictionary<string, SystemEvent> _savedEvent = new Dictionary<string, SystemEvent>();

        public static bool RegistEvent(string eventName, SystemEvent eventDelegate)
        {
            if (_savedEvent.ContainsKey(eventName))
            {
                return false;
            }
            _savedEvent.Add(eventName, eventDelegate);
            return true;
        }

        public static bool RegistEventHandler(string eventName, SystemEvent eventDelegate)
        {
            if (_savedEvent.ContainsKey(eventName))
            {
                _savedEvent[eventName] += eventDelegate;
                return true;
            }

            return false;
        }
    }
}