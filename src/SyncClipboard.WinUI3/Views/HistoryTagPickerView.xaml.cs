using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using Windows.System;
using Windows.UI.Core;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.WinUI3.Win32;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HistoryTagPickerView : UserControl
{
    public HistoryTagPickerViewModel ViewModel { get; } = null!;

    internal bool ClosedByKeyboard { get; private set; }

    private Flyout? _flyout;
    private HistoryTagPickerKeyboard? _keyboard;
    private bool _isComposing;

    public HistoryTagPickerView() : this(null!) { }

    public HistoryTagPickerView(HistoryTagPickerViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PreviewKeyDown += Picker_PreviewKeyDown;
        KeyDown += Picker_KeyDown;
        PreviewKeyUp += Picker_PreviewKeyUp;
        _TagSearch.TextCompositionStarted += (_, _) => _isComposing = true;
        _TagSearch.TextCompositionEnded += (_, _) => _isComposing = false;
    }

    public Flyout ShowAt(FrameworkElement anchor)
    {
        var flyout = new Flyout
        {
            Content = this,
            FlyoutPresenterStyle = (Style)Resources["TagPickerFlyoutStyle"]
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

    public void FocusSearch() => DispatcherQueue.TryEnqueue(() => _TagSearch.Focus(FocusState.Programmatic));

    private void Picker_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_keyboard?.HandleKeyUp(KeyboardMap.ConvertFromVirtualKey(e.Key)) == true)
            e.Handled = true;
    }

    private void Picker_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Native text input needs unhandled text keys; only consume the close action here.
        if (e.Key != VirtualKey.Escape || _isComposing)
            return;
        e.Handled = true;
        ClosedByKeyboard = true;
        _flyout?.Hide();
    }

    private async void Picker_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isComposing || _keyboard is null)
            return;
        if (_keyboard.TryHandleKeyDown(KeyboardMap.ConvertFromVirtualKey(e.Key), IsKeyDown(VirtualKey.Shift),
            HasCommandModifier(), e.KeyStatus.WasKeyDown, out var operation))
        {
            e.Handled = true;
            await operation;
        }
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static bool HasCommandModifier() => IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.Menu)
        || IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows);

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.SelectedSuggestion) or nameof(ViewModel.Suggestions))
            DispatcherQueue.TryEnqueue(ScrollToSelection);
    }

    private void ScrollToSelection()
    {
        if (_flyout is null)
            return;
        UpdateLayout();
        SuggestionButtons().FirstOrDefault(button => Equals(button.Tag, ViewModel.SelectedSuggestion?.Name))
            ?.StartBringIntoView();
    }

    private IEnumerable<Button> SuggestionButtons()
    {
        for (var i = 0; i < _Suggestions.Items.Count; i++)
        {
            if (_Suggestions.ContainerFromIndex(i) is DependencyObject container && FindButton(container) is { } button)
                yield return button;
        }
    }

    private static Button? FindButton(DependencyObject root)
    {
        if (root is Button button)
            return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindButton(VisualTreeHelper.GetChild(root, i)) is { } child)
                return child;
        }
        return null;
    }

    private async void Suggestion_Click(object sender, RoutedEventArgs _)
    {
        _TagSearch.Focus(FocusState.Programmatic);
        if (sender is Button { Tag: string name })
            await ViewModel.ToggleAsync(name);
    }
}
