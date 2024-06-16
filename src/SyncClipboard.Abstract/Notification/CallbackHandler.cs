namespace SyncClipboard.Abstract.Notification;

public class CallbackHandler<NotificationIdType> where NotificationIdType : notnull
{
    private readonly Dictionary<NotificationIdType, Dictionary<string, Button>> _handlerList = new();
    private readonly Dictionary<NotificationIdType, NotificationSessionBase<NotificationIdType>> _sessionList = new();

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
        if (!_sessionList.ContainsKey(id))
        {
            _sessionList.Add(id, session);
        }

        if (!_handlerList.ContainsKey(id))
        {
            _handlerList.Add(id, new());
        }
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