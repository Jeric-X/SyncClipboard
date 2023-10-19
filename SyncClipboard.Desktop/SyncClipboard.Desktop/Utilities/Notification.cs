using SyncClipboard.Abstract;
using System;

namespace SyncClipboard.Desktop.Utilities;

internal class Notification : INotification
{
    public IProgressBar CreateProgressNotification(string title)
    {
        throw new NotImplementedException();
    }

    public void SendImage(string title, string text, Uri uri, params Button[] buttons)
    {
        throw new NotImplementedException();
    }

    public void SendText(string title, string text, params Button[] buttons)
    {
        throw new NotImplementedException();
    }
}
