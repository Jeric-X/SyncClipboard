using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryWindow : Window, IWindow
{
    private readonly HistoryViewModel _viewModel;
    public HistoryViewModel ViewModel => _viewModel;
    public HistoryWindow()
    {
        _viewModel = App.Current.Services.GetRequiredService<HistoryViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Hide();
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
            this.Show();
            this.Activate();
        }
        else
        {
            this.Hide();
        }
    }

    public void Focus()
    {
        this.Show();
        this.Activate();
    }

    #region Manually Handle Click and Double Click
    //private const int DoubleClickThreshold = 300;
    //private CancellationTokenSource? _cts;

    //private void ItemPressed(object? sender, PointerPressedEventArgs e)
    //{
    //    var record = (HistoryRecord?)((Grid?)sender)?.DataContext;
    //    if (record == null)
    //    {
    //        return;
    //    }

    //    _cts?.Cancel();
    //    _cts = new CancellationTokenSource();
    //    _ = DelayTriggerClickEvent(record, e, _cts.Token);
    //}

    //private async Task DelayTriggerClickEvent(HistoryRecord record, PointerPressedEventArgs e, CancellationToken token)
    //{
    //    if (e.ClickCount >= 2)
    //    {
    //        await _viewModel.CopyToClipboard(record, false, token);
    //        this.Hide();
    //        return;
    //    }

    //    await Task.Delay(DoubleClickThreshold, token);
    //    if (e.ClickCount == 1)
    //    {
    //        this.Hide();
    //        await _viewModel.CopyToClipboard(record, true, token);
    //    }
    //}
    #endregion

    private async void ListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        var history = ((ListBox?)sender)?.SelectedValue;
        if (history is not HistoryRecord record)
        {
            return;
        }

        if (e.Key == Key.Enter && (e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Alt))
        {
            e.Handled = true;
            this.Hide();
            var paste = e.KeyModifiers != KeyModifiers.Alt;
            await _viewModel.CopyToClipboard(record, paste, CancellationToken.None);
        }
    }

    private void PasteButtonClicked(object? sender, RoutedEventArgs e)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is not HistoryRecord record)
        {
            return;
        }

        e.Handled = true;
        this.Hide();
        _ = _viewModel.CopyToClipboard(record, true, CancellationToken.None);
    }

    private void CopyButtonClicked(object? sender, RoutedEventArgs e)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is not HistoryRecord record)
        {
            return;
        }

        e.Handled = true;
        this.Hide();
        _ = _viewModel.CopyToClipboard(record, false, CancellationToken.None);
    }

    private void ListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        var history = ((ListBox?)sender)?.SelectedValue;
        if (history is not HistoryRecord record)
        {
            return;
        }
        this.Hide();
        _ = _viewModel.CopyToClipboard(record, false, CancellationToken.None);
    }
}