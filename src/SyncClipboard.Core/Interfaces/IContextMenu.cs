namespace SyncClipboard.Core.Interfaces;

public interface IContextMenu
{
    public const string DefaultGroupName = "Default Group";
    public void AddMenuItemGroup(MenuItem[] menuItems, string? group = null);
    public void AddMenuItem(MenuItem menuItem, string? group = null);
}