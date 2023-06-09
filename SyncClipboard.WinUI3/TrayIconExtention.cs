using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;


namespace SyncClipboard.WinUI3
{
    public static class TrayIconExtention
    {
        public static void TrayIconAdd(this TaskbarIcon tb, MenuFlyoutItemBase flyoutItemBase)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var pInfoContextMenuFlyout = typeof(TaskbarIcon).GetProperty("ContextMenuFlyout", flags);
            var pInfoIsContextMenuVisible = typeof(TaskbarIcon).GetProperty("IsContextMenuVisible", flags);
            var pInfoContextMenuWindowHandle = typeof(TaskbarIcon).GetProperty("ContextMenuWindowHandle", flags);

            var flyout = pInfoContextMenuFlyout.GetValue(tb, null) as MenuFlyout;
            var handle = pInfoContextMenuWindowHandle.GetValue(tb) as nint?;

            if (flyoutItemBase is not MenuFlyoutSeparator)
            {
                flyoutItemBase.Height = 32;
                flyoutItemBase.Padding = new Thickness(11, 0, 11, 0);
            }

            flyout.Items.Add(flyoutItemBase);
            flyoutItemBase.Tapped += (_, _) =>
            {
                pInfoIsContextMenuVisible.SetValue(tb, false);
                flyout.Hide();
                _ = WindowUtilities.HideWindow(handle.Value);
            };
        }
    }
}