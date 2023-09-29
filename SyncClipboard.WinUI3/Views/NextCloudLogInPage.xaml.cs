using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Notification;
using SyncClipboard.Core.ViewModels;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class NextCloudLogInPage : Page
{
    private readonly NextCloudLogInViewModel _viewModel;

    public NextCloudLogInPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<NextCloudLogInViewModel>();
    }

    private Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    private double BoolToHide(Microsoft.UI.Xaml.Controls.ProgressBar progressBar, bool value)
    {
        this.DispatcherQueue.TryEnqueue(() => progressBar.IsEnabled = value);
        return value ? 1 : 0;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ((SettingWindow)App.Current.Services.GetRequiredService<IMainWindow>()).EnableScrollViewer();
        _viewModel.Cancel();
        base.OnNavigatedFrom(e);
    }

    protected override void OnNavigatedTo(NavigationEventArgs _)
    {
        ((SettingWindow)App.Current.Services.GetRequiredService<IMainWindow>()).DispableScrollViewer();
    }

    private async void TreeView_ExpandingAsync(TreeView _, TreeViewExpandingEventArgs args)
    {
        _TreeView.IsEnabled = false;
        await _viewModel.SetChildren((FileTreeViewModel)args.Item);
        _TreeView.IsEnabled = true;
    }

    private bool ItemSeleted(object? obj)
    {
        return obj is not null;
    }

    private void Button_Click(object _, RoutedEventArgs _1)
    {
        try
        {
            _viewModel.SetFolder((FileTreeViewModel)_TreeView.SelectedItem);
            ((SettingWindow)App.Current.Services.GetRequiredService<IMainWindow>()).NavigateToLastLevel();
        }
        catch (Exception ex)
        {
            App.Current.Services.GetRequiredService<NotificationManager>().SendText("设置失败", ex.Message);
        }
    }
}
