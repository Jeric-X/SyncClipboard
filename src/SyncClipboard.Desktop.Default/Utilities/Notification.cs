using SyncClipboard.Abstract.Notification;
using SyncClipboard.Desktop.Utilities;
using System.Runtime.Versioning;
using Tmds.DBus;

namespace SyncClipboard.Desktop.Default.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class Notification : INotification, IDisposable
{
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
        new NotificationSession(new NotificationPara(title, text)
        {
            Image = uri,
            Buttons = buttons
        }, _dBusInstance, _callbackHandler).Show();
    }

    public void SendText(string title, string text, params Button[] buttons)
    {
        new NotificationSession(new NotificationPara(title, text)
        {
            Buttons = buttons
        }, _dBusInstance, _callbackHandler).Show();
    }

    private NotificationSession? _tempSession;
    public void SendTemporary(NotificationPara para)
    {
        lock (this)
        {
            _tempSession?.Remove();
            _tempSession = new NotificationSession(para, _dBusInstance, _callbackHandler)
            {
                Duration = para.Duration ?? TimeSpan.FromSeconds(2.0),
            };
            _tempSession.Show();
        }
    }

    public void Dispose()
    {
        _disposables.ForEach(disposable => disposable.Dispose());
        GC.SuppressFinalize(this);
    }

    ~Notification() => Dispose();
}
