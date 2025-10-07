using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NativeNotification.Interface;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.Utilities;
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

        _SetFolderButton.IsEnabled = false;
        _TreeView.RegisterPropertyChangedCallback(TreeView.SelectedItemProperty, (_, _) =>
        {
            _SetFolderButton.IsEnabled = _TreeView.SelectedItem is not null;
        });
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.Current.MainWindow.DispableScrollViewer();
        base.OnNavigatedTo(e);
    }

    private Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    private double BoolToHide(ProgressBar progressBar, bool value)
    {
        this.DispatcherQueue.TryEnqueue(() => progressBar.IsEnabled = value);
        return value ? 1 : 0;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Current.MainWindow.EnableScrollViewer();
        _viewModel.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async void TreeView_ExpandingAsync(TreeView _, TreeViewExpandingEventArgs args)
    {
        _TreeView.IsEnabled = false;
        await _viewModel.SetChildren((FileTreeViewModel)args.Item);
        _TreeView.IsEnabled = true;
    }

    private void Button_Click(object _, RoutedEventArgs _1)
    {
        try
        {
            _viewModel.SetFolder((FileTreeViewModel)_TreeView.SelectedItem);
        }
        catch (Exception ex)
        {
            var notification = App.Current.Services.GetRequiredService<INotificationManager>();
            notification.ShowText(Core.I18n.Strings.FailedToSet, ex.Message);
        }
    }
}
