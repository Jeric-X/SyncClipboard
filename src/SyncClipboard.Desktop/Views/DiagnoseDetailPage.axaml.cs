using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using SyncClipboard.Desktop.ViewModels;
using System;

namespace SyncClipboard.Desktop.Views;

public partial class DiagnoseDetailPage : UserControl
{
    private readonly DiagnoseDetailViewModel _viewModel;
    public DiagnoseDetailPage()
    {
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        _viewModel = new DiagnoseDetailViewModel();
        DataContext = _viewModel;
        InitializeComponent();
    }

    private async void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        var type = e.Parameter as string;



        var clipboard = await App.Current.Clipboard.GetDataAsync(type!);

        if (clipboard is byte[] byteclipboard)
        {
            _viewModel.Text = System.Text.Encoding.Unicode.GetString(byteclipboard);
        }
        else
        {
            _viewModel.Text = clipboard?.ToString();
        }
    }
}
