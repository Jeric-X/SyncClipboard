using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Linq;
using System.ComponentModel;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Styling;
using SyncClipboard.Desktop.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryTagPickerView : UserControl
{
    public HistoryTagPickerViewModel ViewModel => (HistoryTagPickerViewModel)DataContext!;

    internal bool ClosedByKeyboard { get; private set; }

    private Flyout? _flyout;
    private Key? _activationKey;

    public HistoryTagPickerView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, Picker_PreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, Picker_KeyDown);
        AddHandler(KeyUpEvent, Picker_PreviewKeyUp, RoutingStrategies.Tunnel);
    }

    public HistoryTagPickerView(HistoryTagPickerViewModel viewModel) : this() => DataContext = viewModel;

    public Flyout ShowAt(Control anchor)
    {
        var flyout = new HistoryTagFlyout
        {
            Content = this,
            FlyoutPresenterTheme = (ControlTheme)Resources["TagPickerFlyoutTheme"]!
        };
        ClosedByKeyboard = false;
        _activationKey = null;
        _flyout = flyout;
        flyout.Opened += (_, _) => FocusSearch();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        flyout.Closed += (_, _) =>
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _flyout = null;
        };
        flyout.ShowAt(anchor);
        return flyout;
    }

    public void FocusSearch() => Dispatcher.UIThread.Post(() => _TagSearch.Focus(), DispatcherPriority.Input);

    private void Picker_PreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == _activationKey)
        {
            _activationKey = null;
            e.Handled = true;
        }
    }

    private void Picker_KeyDown(object? sender, KeyEventArgs e)
    {
        // Native text input needs unhandled text keys; only consume the close action here.
        if (e.Key != Key.Escape || IsComposing)
            return;
        e.Handled = true;
        ClosedByKeyboard = true;
        _flyout?.Hide();
    }

    private bool IsComposing => _TagSearch.GetVisualDescendants().OfType<TextPresenter>()
        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));

    private async void Picker_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsComposing)
            return;
        if (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
        {
            e.Handled = true;
            ViewModel.MoveSelection(!e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down)
        {
            e.Handled = true;
            ViewModel.MoveSelection(e.Key == Key.Down);
        }
        else if (e.KeyModifiers == KeyModifiers.None
            && (e.Key == Key.Enter || (e.Key == Key.Space && ViewModel.Search.Length == 0)))
        {
            e.Handled = true;
            if (_activationKey is not null)
                return;
            _activationKey = e.Key;
            await ViewModel.ToggleSelectedAsync();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.SelectedSuggestion) or nameof(ViewModel.Suggestions))
            Dispatcher.UIThread.Post(ScrollToSelection, DispatcherPriority.Loaded);
    }

    private void ScrollToSelection()
    {
        if (_flyout is null)
            return;
        UpdateLayout();
        _Suggestions.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
            ReferenceEquals(button.DataContext, ViewModel.SelectedSuggestion))?.BringIntoView();
    }

    private async void Suggestion_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _TagSearch.Focus();
        if (sender is Button { DataContext: HistoryTagSuggestion suggestion })
            await ViewModel.ToggleAsync(suggestion.Name);
    }
}
