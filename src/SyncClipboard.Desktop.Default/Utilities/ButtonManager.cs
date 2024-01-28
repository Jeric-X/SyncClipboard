namespace SyncClipboard.Desktop.Default.Utilities;

public class ButtonManager<NotificationIdType, ButtonIdType> where ButtonIdType : notnull where NotificationIdType : notnull
{
    private readonly Dictionary<NotificationIdType, Dictionary<ButtonIdType, Action<string>>> _handlerList = new();

    public void OnActive(NotificationIdType id, ButtonIdType buttonId)
    {
        var found = _handlerList.TryGetValue(id, out Dictionary<ButtonIdType, Action<string>>? buttonList);
        if (found)
        {
            buttonList!.TryGetValue(buttonId, out Action<string>? buttonHandler);
            buttonHandler?.Invoke("");
            _handlerList.Remove(id);
        }
    }

    public void OnClosed(NotificationIdType id)
    {
        _handlerList.Remove(id);
    }

    public bool AddHandler(NotificationIdType id, ButtonIdType buttonId, Action<string> handler)
    {
        if (!_handlerList.ContainsKey(id))
        {
            _handlerList.Add(id, new());
        }
        var buttonList = _handlerList[id];
        buttonList.Add(buttonId, handler);
        return true;
    }
}