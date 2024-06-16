using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Commons;

namespace SyncClipboard.Desktop.Default.Utilities;

internal class NotificationSession : NotificationSessionBase<uint>
{
    private static readonly string AppIcon = new Uri(Path.Combine(Env.ProgramDirectory, "Assets", "icon.svg")).AbsoluteUri;

    private readonly IDbusNotifications _dBusInstance;
    private readonly CallbackHandler<uint> _callbackHandler;

    private uint? _notificationId;
    protected override uint NativeNotificationId => _notificationId ?? 0;

    public override string Title { get; set; }
    private string Text { get; set; }

    public NotificationSession(
        NotificationPara notificationPara, IDbusNotifications dbusNotifications, CallbackHandler<uint> callbackHandler)
        : base(callbackHandler)
    {
        _dBusInstance = dbusNotifications;
        _callbackHandler = callbackHandler;

        Title = notificationPara.Title;
        Buttons = notificationPara.Buttons.ToList();
        Image = notificationPara.Image;
        Text = notificationPara.Text;
    }

    protected override void NativeRemove()
    {
        _dBusInstance.CloseNotificationAsync(NativeNotificationId);
    }

    protected override void NativeShow()
    {
        List<string> actionList = new();
        foreach (var button in Buttons)
        {
            actionList.Add(button.Uid.ToString());
            actionList.Add(button.Text);
        }

        var hintDictionary = new Dictionary<string, object>();
        if (Image is not null)
        {
            hintDictionary.Add("image-path", Image.AbsoluteUri);
        }

        _notificationId = Task.Run(async () =>
           await _dBusInstance.NotifyAsync(
               Env.SoftName,
               NativeNotificationId,
               AppIcon,
               Title,
               Text,
               actionList.ToArray(),
               hintDictionary,
               ((int?)Duration?.TotalMilliseconds) ?? 0
        )
        ).Result;

        _callbackHandler.AddButtons(NativeNotificationId, Buttons, this);
    }

    protected override void NativeShowSilent()
    {
        NativeShow();
    }
}
