using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SyncClipboard.Desktop.Utilities;

internal static class HistoryListScrollAnchor
{
    public static void PreserveSelectedItemPosition(ListBox list)
    {
        var item = list.SelectedItem;
        if (item is null || list.Scroll is not ScrollViewer scroll ||
            list.ContainerFromItem(item) is not Control container ||
            container.TranslatePoint(default, scroll) is not { } position ||
            position.Y >= scroll.Viewport.Height || position.Y + container.Bounds.Height <= 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(list.SelectedItem, item) || !list.IsAttachedToVisualTree())
                return;

            list.UpdateLayout();
            list.ScrollIntoView(item);
            list.UpdateLayout();
            if (list.ContainerFromItem(item) is Control current &&
                current.TranslatePoint(default, scroll) is { } currentPosition)
            {
                scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + currentPosition.Y - position.Y);
            }
        }, DispatcherPriority.Loaded);
    }
}
