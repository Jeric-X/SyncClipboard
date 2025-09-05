using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using System;
using System.Threading;
using SyncClipboard.Core.ViewModels.Sub;

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
        this.Activated += (_, _) => _viewModel.OnGotFocus();

        Height = _viewModel.Height;
        Width = _viewModel.Width;
        this.SizeChanged += (_, _) =>
        {
            _viewModel.Height = (int)Height;
            _viewModel.Width = (int)Width;
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
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
    }

    void IWindow.Focus()
    {
        FocusOnScreen();
    }

    private async void ListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        var history = ((ListBox?)sender)?.SelectedValue;
        if (history is not HistoryRecordVM record)
        {
            return;
        }

        if (e.Key == Key.Enter && (e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Alt))
        {
            e.Handled = true;
            var paste = e.KeyModifiers != KeyModifiers.Alt;
            await _viewModel.CopyToClipboard(record, paste, CancellationToken.None);
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
        _ = _viewModel.CopyToClipboard(record, false, CancellationToken.None);
    }

    private void Image_DoubleTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        var history = ((Image?)sender)?.DataContext;
        if (history is not HistoryRecordVM record)
        {
            return;
        }
        _viewModel.ViewImage(record);
    }

    private void Image_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        if (image.Bounds.Size.Height > 200)
        {
            image.MaxHeight = 200;
            image.Stretch = Stretch.Uniform;
        }
    }

    public void ScrollToTop()
    {
        if (_ListBox.ItemCount != 0)
        {
            _ListBox.ScrollIntoView(0);
        }
    }
}