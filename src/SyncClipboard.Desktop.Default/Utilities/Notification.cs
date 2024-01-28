using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using System.Runtime.Versioning;
using Tmds.DBus;

namespace SyncClipboard.Desktop.Default.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class Notification : INotification, IDisposable
{
    private static readonly string AppIcon = new Uri(Path.Combine(Env.Directory, "Assets", "icon.svg")).AbsoluteUri;
    private readonly IDbusNotifications _dBusInstance;
    private readonly ButtonManager<uint, string> _buttonManager = new();
    private readonly List<IDisposable> _disposables = new();

    public Notification()
    {
        var connection = Connection.Session;
        _dBusInstance = connection.CreateProxy<IDbusNotifications>("org.freedesktop.Notifications", "/org/freedesktop/Notifications");
        Task.Run(async () =>
        {
            _disposables.Add(await _dBusInstance.WatchActionInvokedAsync(input => _buttonManager.OnActive(input.id, input.actionKey)));
            _disposables.Add(await _dBusInstance.WatchNotificationClosedAsync(input => _buttonManager.OnClosed(input.id)));
        }).Wait();
    }

    public IProgressBar CreateProgressNotification(string title)
    {
        return new ProgressBar();
    }

    public void SendImage(string title, string text, Uri uri, params Button[] buttons)
    {
        SendNotification(title, text, uri, buttons);
    }

    public void SendText(string title, string text, params Button[] buttons)
    {
        SendNotification(title, text, null, buttons);
    }

    private void SendNotification(string title, string text, Uri? imageUri = null, params Button[] buttons)
    {
        List<string> actionList = new();
        foreach (var button in buttons)
        {
            actionList.Add(button.Callbacker?.Argument ?? "");
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
               0
           )
        ).Result;

        foreach (var button in buttons.Where(button => button.Callbacker is not null))
        {
            _buttonManager.AddHandler(id, button.Callbacker!.Argument, button.Callbacker.CallBack);
        }
    }

    public void Dispose()
    {
        _disposables.ForEach(disposable => disposable.Dispose());
        GC.SuppressFinalize(this);
    }

    ~Notification() => Dispose();
}
