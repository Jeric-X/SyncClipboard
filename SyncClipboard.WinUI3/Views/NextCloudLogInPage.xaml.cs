using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SyncClipboard.Core.ViewModels;

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

    private double BoolToHide(bool value)
    {
        return value ? 1 : 0;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _viewModel.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void TreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {

    }
}
