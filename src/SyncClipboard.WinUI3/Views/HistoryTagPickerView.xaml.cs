using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HistoryTagPickerView : UserControl
{
    public HistoryTagPickerViewModel ViewModel { get; } = null!;

    public HistoryTagPickerView() => InitializeComponent();

    public HistoryTagPickerView(HistoryTagPickerViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public Flyout ShowAt(FrameworkElement anchor)
    {
        var flyout = new Flyout
        {
            Content = this,
            FlyoutPresenterStyle = (Style)Resources["TagPickerFlyoutStyle"]
        };
        flyout.Opened += (_, _) => FocusSearch();
        flyout.ShowAt(anchor);
        return flyout;
    }

    public void FocusSearch() => DispatcherQueue.TryEnqueue(() => _TagSearch.Focus(FocusState.Programmatic));

    private async void AddInput_Click(object _, RoutedEventArgs _1)
    {
        _TagSearch.Focus(FocusState.Programmatic);
        await ViewModel.AddInputAsync();
    }

    private async void Suggestion_Click(object sender, RoutedEventArgs _)
    {
        _TagSearch.Focus(FocusState.Programmatic);
        if (sender is Button { Tag: string name })
            await ViewModel.ToggleAsync(name);
    }
}
