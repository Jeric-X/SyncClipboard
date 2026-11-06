using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Styling;
using SyncClipboard.Desktop.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryTagPickerView : UserControl
{
    public HistoryTagPickerViewModel ViewModel => (HistoryTagPickerViewModel)DataContext!;

    public HistoryTagPickerView() => InitializeComponent();

    public HistoryTagPickerView(HistoryTagPickerViewModel viewModel) : this() => DataContext = viewModel;

    public Flyout ShowAt(Control anchor)
    {
        var flyout = new HistoryTagFlyout
        {
            Content = this,
            FlyoutPresenterTheme = (ControlTheme)Resources["TagPickerFlyoutTheme"]!
        };
        flyout.Opened += (_, _) => FocusSearch();
        flyout.ShowAt(anchor);
        return flyout;
    }

    public void FocusSearch() => Dispatcher.UIThread.Post(() => _TagSearch.Focus(), DispatcherPriority.Input);

    private async void AddInput_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _TagSearch.Focus();
        await ViewModel.AddInputAsync();
    }

    private async void Suggestion_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _TagSearch.Focus();
        if (sender is Button { DataContext: HistoryTagSuggestion suggestion })
            await ViewModel.ToggleAsync(suggestion.Name);
    }
}
