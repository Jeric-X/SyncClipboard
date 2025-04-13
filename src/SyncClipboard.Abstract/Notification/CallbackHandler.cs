namespace SyncClipboard.Abstract.Notification;

public class CallbackHandler<NotificationIdType> where NotificationIdType : notnull
{
    private readonly Dictionary<NotificationIdType, Dictionary<string, Button>> _handlerList = [];
    private readonly Dictionary<NotificationIdType, NotificationSessionBase<NotificationIdType>> _sessionList = [];

    public void OnActivated(NotificationIdType id, string buttonId)
    {
        var found = _handlerList.TryGetValue(id, out Dictionary<string, Button>? buttonList);
        if (found)
        {
            buttonList!.TryGetValue(buttonId, out Button? button);
            button?.Invoke();
            _handlerList.Remove(id);
        }

        _sessionList.Remove(id);
    }

    public void OnClosed(NotificationIdType id)
    {
        _handlerList.Remove(id);
        _sessionList.Remove(id);
    }

    public void AddButton(NotificationIdType id, Button button, NotificationSessionBase<NotificationIdType> session)
    {
        _sessionList.TryAdd(id, session);
        _handlerList.TryAdd(id, []);

        var buttonList = _handlerList[id];
        buttonList.Add(button.Uid.ToString(), button);
    }

    public void AddButtons(NotificationIdType id, IEnumerable<Button> buttons, NotificationSessionBase<NotificationIdType> session)
    {
        foreach (var item in buttons)
        {
            AddButton(id, item, session);
        }
    }
}