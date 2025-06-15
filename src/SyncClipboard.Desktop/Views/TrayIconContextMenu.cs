using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.AbstractClasses;
using SyncClipboard.Core.Interfaces;
using System;

namespace SyncClipboard.Desktop.Views;

internal class TrayIconContextMenu : ContextMenuBase
{
    private readonly NativeMenu _menu;
    private readonly int _menuReserveCount;

    public TrayIconContextMenu()
    {
        var icons = TrayIcon.GetIcons(App.Current);
        var trayIcon = icons?[0];
        var menu = trayIcon?.Menu;
        ArgumentNullException.ThrowIfNull(menu, nameof(menu));
        _menu = menu;
        _menuReserveCount = _menu.Items.Count;
    }

    private void InsertItem(int index, NativeMenuItemBase menuItemBase)
    {
        _menu.Items.Insert(index, menuItemBase);
    }

    protected override void InsertMenuItem(int index, Core.Interfaces.MenuItem menuitem)
    {
        NativeMenuItem item = new()
        {
            Header = menuitem.Text,
        };

        if (menuitem.Action is not null)
        {
            item.Command = new RelayCommand(menuitem.Action);
        }
        InsertItem((ushort)index, item);
    }

    protected override void InsertSeparator(int index)
    {
        InsertItem(index, new NativeMenuItemSeparator());
    }

    protected override void InsertToggleMenuItem(int index, ToggleMenuItem menuitem)
    {
        NativeMenuItem item = new()
        {
            Header = menuitem.Text,
            IsChecked = menuitem.Checked,
            ToggleType = NativeMenuItemToggleType.CheckBox
        };

        menuitem.CheckedChanged += status => Dispatcher.UIThread.Post(() => item.IsChecked = status);

        if (menuitem.Action is not null)
        {
            item.Command = new RelayCommand(menuitem.Action);
        }
        InsertItem((ushort)index, item);
    }

    protected override int MenuItemsCount()
    {
        return _menu.Items.Count - _menuReserveCount;
    }
}
