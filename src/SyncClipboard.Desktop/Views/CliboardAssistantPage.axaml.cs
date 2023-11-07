using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class CliboardAssistantPage : UserControl
{
    public CliboardAssistantPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.GetRequiredService<CliboardAssistantViewModel>();
    }
}
