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
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Desktop.Utilities;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryTagPickerView : UserControl
{
    public HistoryTagPickerViewModel ViewModel => (HistoryTagPickerViewModel)DataContext!;

    internal bool ClosedByKeyboard { get; private set; }

    private Flyout? _flyout;
    private HistoryTagPickerKeyboard? _keyboard;

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
        _keyboard = new HistoryTagPickerKeyboard(ViewModel);
        _flyout = flyout;
        flyout.Opened += (_, _) => FocusSearch();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        flyout.Closed += (_, _) =>
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _flyout = null;
            _keyboard = null;
        };
        flyout.ShowAt(anchor);
        return flyout;
    }

    public void FocusSearch() => Dispatcher.UIThread.Post(() => _TagSearch.Focus(), DispatcherPriority.Input);

    private void Picker_PreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (_keyboard?.HandleKeyUp(KeyboardMap.ConvertFromAvalonia(e.Key)) == true)
            e.Handled = true;
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
        if (IsComposing || _keyboard is null)
            return;
        // macOS masks printable keys for composing input sources before preedit starts.
        var key = e.Key == Key.ImeProcessed && e.PhysicalKey == PhysicalKey.Space ? Key.Space : e.Key;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var command = (e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None;
        if (_keyboard.TryHandleKeyDown(KeyboardMap.ConvertFromAvalonia(key), shift, command, false, out var operation))
        {
            e.Handled = true;
            await operation;
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
