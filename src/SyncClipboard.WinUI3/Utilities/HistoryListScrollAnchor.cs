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
        {
            if (!ReferenceEquals(list.SelectedItem, item) || !list.IsLoaded)
                return;

            list.UpdateLayout();
            if (list.ContainerFromItem(item) is null)
            {
                list.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
                list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, RestorePosition);
            }
            else
            {
                RestorePosition();
            }
        });

        void RestorePosition()
        {
            if (!ReferenceEquals(list.SelectedItem, item) || !list.IsLoaded)
                return;

            list.UpdateLayout();
            if (list.ContainerFromItem(item) is FrameworkElement current)
            {
                var currentPosition = current.TransformToVisual(scroll).TransformPoint(new Point());
                scroll.ChangeView(null, scroll.VerticalOffset + currentPosition.Y - position.Y, null, disableAnimation: true);
            }
        }
    }
}
