namespace SyncClipboard.Core.Interface
{
    public class MenuItem
    {
        public string? Text { get; init; }
        public Action? Action { get; init; }
    }

    public interface IContextMenu
    {
        public void AddMenuItemGroup(MenuItem[] menuItems);
    }
}