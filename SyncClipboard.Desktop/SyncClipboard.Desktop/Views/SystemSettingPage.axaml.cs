using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class SystemSettingPage : UserControl
{
    public SystemSettingPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.GetRequiredService<SystemSettingViewModel>();
    }
}
