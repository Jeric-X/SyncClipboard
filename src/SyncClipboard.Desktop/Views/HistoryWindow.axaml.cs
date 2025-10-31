using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using System;
using System.Threading;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryWindow : Window, IWindow
{
    private readonly HistoryViewModel _viewModel;
    public HistoryViewModel ViewModel => _viewModel;
    public HistoryWindow()
    {
        _viewModel = App.Current.Services.GetRequiredService<HistoryViewModel>();
        var configManager = App.Current.Services.GetRequiredService<ConfigManager>();
        DataContext = ViewModel;

        this.ExtendClientAreaToDecorationsHint = true;
        this.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;

        InitializeComponent();
        InitializeScrollWatcher();
        SetWindowMinSize();
        this.Loaded += async (_, _) =>
        {
            await _viewModel.Init(this);
        };

        this.Deactivated += (_, _) => _viewModel.OnLostFocus();
        this.Activated += (_, _) =>
        {
            _viewModel.OnGotFocus();
            _SearchTextBox.Focus();
        };

        Height = _viewModel.Height;
        Width = _viewModel.Width;
        this.SizeChanged += (_, _) =>
        {
            _viewModel.Height = (int)Height;
            _viewModel.Width = (int)Width;
        };

        this.Topmost = _viewModel.IsTopmost;
    }

    private void SetWindowMinSize()
    {
        var infiniteSize = new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity);
        _FilterSelectorBar.Measure(infiniteSize);
        _ButtonArea.Measure(infiniteSize);
        _SearchTextBox.Measure(infiniteSize);

        MinWidth = _FilterSelectorBar.DesiredSize.Width + _ButtonArea.DesiredSize.Width * 2;
        MinHeight = _FilterSelectorBar.DesiredSize.Height + _SearchTextBox.DesiredSize.Height;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _SearchTextBox.Focus();
            _SearchTextBox.SelectAll();
            e.Handled = true;
            return;
        }

        var isShiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var isAltPressed = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var isCtrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        var key = Utilities.KeyboardMap.ConvertFromAvalonia(e.Key);

        if (!key.HasValue)
        {
            throw new NotSupportedException($"Avalonia key '{e.Key}' is not supported by KeyboardMap. Please add mapping for this key.");
        }

        var handled = _viewModel.HandleKeyPress(key.Value, isShiftPressed, isAltPressed, isCtrlPressed);

        if (handled)
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.ApplicationShutdown || e.CloseReason == WindowCloseReason.OSShutdown)
        {
            base.OnClosing(e);
            return;
        }
        this.Hide();
        e.Cancel = true;
    }

    public void SwitchVisible()
    {
        if (!this.IsVisible)
        {
            FocusOnScreen();
        }
        else
        {
            this.Close();
        }
    }

    protected virtual void FocusOnScreen()
    {
        this.Show();
        if (this.WindowState == WindowState.Minimized)
        {
            this.WindowState = WindowState.Normal;
        }
        this.Activate();

        _SearchTextBox.Focus();
        _SearchTextBox.SelectAll();
        _viewModel.OnWindowShown();
    }

    void IWindow.Focus()
    {
        FocusOnScreen();
    }

    public void ScrollToSelectedItem()
    {
        if (_ListBox.SelectedItem != null)
        {
            _ListBox.ScrollIntoView(_ListBox.SelectedItem);
        }
    }

    private void InitializeScrollWatcher()
    {
        if (_ListBox.GetValue(ListBox.ScrollProperty) is ScrollViewer existing)
        {
            AttachScrollViewerWatcher(existing);
            return;
        }

        void handler(object? s, AvaloniaPropertyChangedEventArgs e)
        {
            try
            {
                if (e.Property != ListBox.ScrollProperty) return;
                if (e.NewValue is not ScrollViewer sv) return;

                _ListBox.PropertyChanged -= handler;
                AttachScrollViewerWatcher(sv);
            }
            catch { }
        }

        _ListBox.PropertyChanged += handler;
    }

    private void AttachScrollViewerWatcher(ScrollViewer scroll)
    {
        _scrollViewer = scroll;
        scroll.PropertyChanged += async (_, e) =>
        {
            if (e.Property != ScrollViewer.OffsetProperty && e.Property != ScrollViewer.ViewportProperty && e.Property != ScrollViewer.ExtentProperty)
                return;

            var offsetY = scroll.Offset.Y;
            var viewport = scroll.Viewport.Height;
            var extent = scroll.Extent.Height;

            await ViewModel.NotifyScrollPositionAsync(offsetY, viewport, extent);
        };
    }

    private ScrollViewer? _scrollViewer = null;

    public bool GetScrollViewMetrics(out double offsetY, out double viewportHeight, out double extentHeight)
    {
        offsetY = 0; viewportHeight = 0; extentHeight = 0;
        if (_scrollViewer != null)
        {
            var offset = _scrollViewer.Offset;
            offsetY = offset.Y;
            viewportHeight = _scrollViewer.Viewport.Height;
            extentHeight = _scrollViewer.Extent.Height;
            return true;
        }
        return false;
    }

    private async void ItemContextFlyout_Opening(object? sender, EventArgs e)
    {
        if (sender is not FAMenuFlyout flyout)
        {
            return;
        }

        HistoryRecordVM? record = null;
        if (flyout.Target is Control placement)
        {
            record = placement.DataContext as HistoryRecordVM;
        }

        if (record is null)
        {
            flyout.Items.Clear();
            return;
        }
        _ListBox.SelectedItem = record;

        var actions = await _viewModel.BuildActionsAsync(record);
        flyout.Items.Clear();
        foreach (var action in actions)
        {
            var item = new MenuFlyoutItem { Text = action.Text };
            if (action.Action is not null)
            {
                item.Click += (_, __) => action.Action();
            }
            flyout.Items.Add(item);
        }
    }

    private void PasteButtonClicked(object? sender, RoutedEventArgs e)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is not HistoryRecordVM record)
        {
            return;
        }

        e.Handled = true;
        _ = _viewModel.CopyToClipboard(record, true, CancellationToken.None);
    }

    private void CopyButtonClicked(object? sender, RoutedEventArgs e)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is not HistoryRecordVM record)
        {
            return;
        }

        e.Handled = true;
        _ = _viewModel.CopyToClipboard(record, false, CancellationToken.None);
    }

    private void ListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        var history = ((ListBox?)sender)?.SelectedValue;
        if (history is not HistoryRecordVM record)
        {
            return;
        }
        _viewModel.HandleItemDoubleClick(record);
    }

    private void Image_DoubleTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        var history = ((Image?)sender)?.DataContext;
        if (history is not HistoryRecordVM record)
        {
            return;
        }
        _viewModel.HandleImageDoubleClick(record);
    }

    private void Image_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        if (image.Source is null)
        {
            image.PropertyChanged += (sender, e) =>
            {
                if (e.Property.Name == "Source" && e.NewValue != null)
                {
                    SetImageVisual(image);
                }
            };
        }
        else
        {
            SetImageVisual(image);
        }
    }

    private void SetImageVisual(Image image)
    {
        _InvisualableImage.Source = image.Source;
        _InvisualableImage.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
        _InvisualableImage.Source = null;
        var desiredSize = _InvisualableImage.DesiredSize;

        if (desiredSize.Height > 200)
        {
            image.Stretch = Stretch.Uniform;
        }
        image.Opacity = 1;
    }

    public void ScrollToTop()
    {
        _scrollViewer?.ScrollToHome();
    }

    public void SetTopmost(bool topmost)
    {
        this.Topmost = topmost;
    }
}