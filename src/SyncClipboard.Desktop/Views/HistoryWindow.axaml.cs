using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryWindow : Window, IWindow
{
    public HistoryViewModel _viewModel { get; set; }
    public HistoryViewModel ViewModel => _viewModel;
    public HistoryWindow()
    {
        _viewModel = App.Current.Services.GetRequiredService<HistoryViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Hide();
            e.Handled = true;
            return;
        }
        base.OnKeyUp(e);
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
            ViewModel.Refresh();
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
}