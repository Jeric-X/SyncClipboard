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

        if (OperatingSystem.IsLinux() is false)
        {
            this.ExtendClientAreaToDecorationsHint = true;
        }

        if (OperatingSystem.IsWindows())
        {
            this.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        }

        InitializeComponent();

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

    private void PinButton_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _viewModel.ToggleTopmost();
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
        if (_ListBox.ItemCount != 0)
        {
            _ListBox.ScrollIntoView(0);
        }
    }

    public void SetTopmost(bool topmost)
    {
        this.Topmost = topmost;
    }
}