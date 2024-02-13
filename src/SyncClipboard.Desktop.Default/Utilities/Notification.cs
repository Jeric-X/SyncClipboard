using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Commons;
using SyncClipboard.Desktop.Utilities;
using System.Runtime.Versioning;
using Tmds.DBus;

namespace SyncClipboard.Desktop.Default.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class Notification : INotification, IDisposable
{
    private static readonly string AppIcon = new Uri(Path.Combine(Env.Directory, "Assets", "icon.svg")).AbsoluteUri;
    private readonly IDbusNotifications _dBusInstance;
    private readonly CallbackHandler<uint> _callbackHandler = new();
    private readonly List<IDisposable> _disposables = new();

    public Notification()
    {
        var connection = Connection.Session;
        _dBusInstance = connection.CreateProxy<IDbusNotifications>("org.freedesktop.Notifications", "/org/freedesktop/Notifications");
        Task.Run(async () =>
        {
            _disposables.Add(await _dBusInstance.WatchActionInvokedAsync(input => _callbackHandler.OnActivated(input.id, input.actionKey)));
            _disposables.Add(await _dBusInstance.WatchNotificationClosedAsync(input => _callbackHandler.OnClosed(input.id)));
        }).Wait();
    }

    public IProgressBar CreateProgressNotification(string title)
    {
        return new ProgressBar();
    }

    public void SendImage(string title, string text, Uri uri, params Button[] buttons)
    {
        SendNotification(title, text, uri, buttons, null);
    }

    public void SendText(string title, string text, params Button[] buttons)
    {
        SendNotification(title, text, null, buttons, null);
    }

    public void Send(NotificationPara para)
    {
        SendNotification(para.Title, para.Text, para.Image, para.Buttons.ToArray(), para.Duration);
    }

    private void SendNotification(string title, string text, Uri? imageUri, Button[] buttons, TimeSpan? timeout)
    {
        List<string> actionList = new();
        foreach (var button in buttons)
        {
            actionList.Add(button.Uid.ToString());
            actionList.Add(button.Text);
        }

        var hintDictionary = new Dictionary<string, object>();
        if (imageUri is not null)
        {
            hintDictionary.Add("image-path", imageUri.AbsoluteUri);
        }

        var id = Task.Run(async () =>
           await _dBusInstance.NotifyAsync(
               Env.SoftName,
               0,
               AppIcon,
               title,
               text,
               actionList.ToArray(),
               hintDictionary,
               ((int?)timeout?.TotalMilliseconds) ?? 0
           )
        ).Result;

        _callbackHandler.AddButtons(id, buttons);
    }

    public void Dispose()
    {
        _disposables.ForEach(disposable => disposable.Dispose());
        GC.SuppressFinalize(this);
    }

    ~Notification() => Dispose();
}
