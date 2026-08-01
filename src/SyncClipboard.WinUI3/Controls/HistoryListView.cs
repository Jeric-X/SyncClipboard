using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace SyncClipboard.WinUI3.Controls;

public sealed class HistoryListView : ListView
{
    public static readonly DependencyProperty SuppressPointerSelectionProperty = DependencyProperty.Register(
        nameof(SuppressPointerSelection),
        typeof(bool),
        typeof(HistoryListView),
        new PropertyMetadata(false));

    public bool SuppressPointerSelection
    {
        get => (bool)GetValue(SuppressPointerSelectionProperty);
        set => SetValue(SuppressPointerSelectionProperty, value);
    }

    public HistoryListView()
    {
        DefaultStyleKey = typeof(ListView);
    }

    protected override DependencyObject GetContainerForItemOverride() => new HistoryListViewItem(this);

    protected override bool IsItemItsOwnContainerOverride(object item) => item is ListViewItem;
}

internal sealed class HistoryListViewItem : ListViewItem
{
    private readonly HistoryListView owner;

    public HistoryListViewItem(HistoryListView owner)
    {
        this.owner = owner;
        DefaultStyleKey = typeof(ListViewItem);
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (owner.SuppressPointerSelection)
        {
            e.Handled = true;
            return;
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (owner.SuppressPointerSelection)
        {
            e.Handled = true;
            return;
        }

        base.OnPointerReleased(e);
    }

    protected override void OnTapped(TappedRoutedEventArgs e)
    {
        if (owner.SuppressPointerSelection)
        {
            e.Handled = true;
            return;
        }

        base.OnTapped(e);
    }
}
