using System;
using System.Collections.Generic;
using Microsoft.Toolkit.Uwp.Notifications;
#nullable enable

namespace SyncClipboard.Utility.Notification
{
    public static class Handler
    {
        private static readonly Dictionary<string, Action<string>> handlerList = new();
        public static void OnActive(ToastNotificationActivatedEventArgsCompat args)
        {
            if (handlerList.ContainsKey(args.Argument))
            {
                handlerList[args.Argument](args.Argument);
                handlerList.Remove(args.Argument);
            }
        }

        public static bool AddHandler(string name, Action<string> handler)
        {
            string adjustName = name;
            if (handlerList.ContainsKey(adjustName))
            {
                return false;
            }
            handlerList.Add(adjustName, handler);
            return true;
        }
    }
}