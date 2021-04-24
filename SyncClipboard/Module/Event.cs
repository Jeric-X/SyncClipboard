using System.Collections.Generic;

namespace SyncClipboard.Module
{
    internal static class Event
    {
        internal delegate void SystemEvent();

        private static Dictionary<string, SystemEvent> _savedEvent = new Dictionary<string, SystemEvent>();

        internal static bool RegistEvent(string eventName, SystemEvent eventDelegate)
        {
            if (_savedEvent.ContainsKey(eventName))
            {
                return false;
            }
            _savedEvent.Add(eventName, eventDelegate);
            return true;
        }

        internal static bool RegistEventHandler(string eventName, SystemEvent eventDelegate)
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