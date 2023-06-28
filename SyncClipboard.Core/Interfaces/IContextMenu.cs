namespace SyncClipboard.Core.Interfaces
{
    public class MenuItem
    {
        public string? Text { get; init; }
        public Action? Action { get; init; }
        public MenuItem(string? text, Action? action)
        {
            Text = text;
            Action = action;
        }
    }

    public interface IContextMenu
    {
        public void AddMenuItemGroup(MenuItem[] menuItems);
    }
}