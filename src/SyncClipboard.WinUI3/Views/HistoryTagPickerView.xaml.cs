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

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HistoryTagPickerView : UserControl
{
    public HistoryTagPickerViewModel ViewModel { get; } = null!;

    internal bool ClosedByKeyboard { get; private set; }

    private Flyout? _flyout;
    private VirtualKey? _activationKey;
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

    public void FocusSearch() => DispatcherQueue.TryEnqueue(() => _TagSearch.Focus(FocusState.Programmatic));

    private void Picker_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == _activationKey)
        {
            _activationKey = null;
            e.Handled = true;
        }
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
        if (_isComposing || HasCommandModifier())
            return;
        var shift = IsKeyDown(VirtualKey.Shift);
        if (e.Key == VirtualKey.Tab)
        {
            e.Handled = true;
            ViewModel.MoveSelection(!shift);
        }
        else if (!shift && e.Key is VirtualKey.Up or VirtualKey.Down)
        {
            e.Handled = true;
            ViewModel.MoveSelection(e.Key == VirtualKey.Down);
        }
        else if (!shift && (e.Key == VirtualKey.Enter || (e.Key == VirtualKey.Space && ViewModel.Search.Length == 0)))
        {
            e.Handled = true;
            if (_activationKey is not null || e.KeyStatus.WasKeyDown)
                return;
            _activationKey = e.Key;
            await ViewModel.ToggleSelectedAsync();
        }
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static bool HasCommandModifier() => new[]
        { VirtualKey.Control, VirtualKey.Menu, VirtualKey.LeftWindows, VirtualKey.RightWindows }.Any(IsKeyDown);

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
