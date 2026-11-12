using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace SyncClipboard.WinUI3.Utilities;

internal static class HistoryListScrollAnchor
{
    public static void PreserveSelectedItemPosition(ListView list, ScrollViewer? scroll)
    {
        var item = list.SelectedItem;
        if (item is null || scroll is null || list.ContainerFromItem(item) is not FrameworkElement container)
            return;

        var position = container.TransformToVisual(scroll).TransformPoint(new Point());
        if (position.Y >= scroll.ViewportHeight || position.Y + container.ActualHeight <= 0)
            return;

        list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            EnsureContainerAndRestorePosition(list, scroll, item, position.Y));
    }

    private static void EnsureContainerAndRestorePosition(ListView list, ScrollViewer scroll, object item, double originalY)
    {
        if (!ReferenceEquals(list.SelectedItem, item) || !list.IsLoaded)
            return;

        list.UpdateLayout();
        if (list.ContainerFromItem(item) is null)
        {
            list.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
            list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => RestorePosition(list, scroll, item, originalY));
        }
        else
        {
            RestorePosition(list, scroll, item, originalY);
        }
    }

    private static void RestorePosition(ListView list, ScrollViewer scroll, object item, double originalY)
    {
        if (!ReferenceEquals(list.SelectedItem, item) || !list.IsLoaded)
            return;

        list.UpdateLayout();
        if (list.ContainerFromItem(item) is FrameworkElement current)
        {
            var currentPosition = current.TransformToVisual(scroll).TransformPoint(new Point());
            scroll.ChangeView(null, scroll.VerticalOffset + currentPosition.Y - originalY, null, disableAnimation: true);
        }
    }
}
