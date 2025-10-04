using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

/// <summary>
/// 添加账号页面
/// </summary>
public sealed partial class AddAccountPage : Page
{
    private readonly AddAccountViewModel _viewModel;

    public AddAccountPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<AddAccountViewModel>();
        this.DataContext = _viewModel;
    }
}