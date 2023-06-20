using SyncClipboard.Core.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.Views
{
    public class TrayIconContextMenu : IContextMenu
    {
        private readonly TrayIcon _trayIcon;
        public TrayIconContextMenu(SettingWindow mainWindow)
        {
            _trayIcon = mainWindow.TrayIcon;
        }

        void IContextMenu.AddMenuItemGroup(MenuItem[] menuItems)
        {
            throw new NotImplementedException();
        }
    }
}
