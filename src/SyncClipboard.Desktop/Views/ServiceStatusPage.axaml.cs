using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views
{
    public partial class ServiceStatusPage : UserControl
    {
        public ServiceStatusPage()
        {
            DataContext = App.Current.Services.GetRequiredService<ServiceStatusViewModel>();
            InitializeComponent();
        }
    }
}
