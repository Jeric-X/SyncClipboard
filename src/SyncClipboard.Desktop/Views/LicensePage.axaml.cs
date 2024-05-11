using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using SyncClipboard.Core.ViewModels;
using System.IO;

namespace SyncClipboard.Desktop.Views;

public partial class LicensePage : UserControl
{
    private readonly LicenseViewModel _viewModel;

    public LicensePage()
    {
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        _viewModel = new LicenseViewModel();
        DataContext = _viewModel;
        InitializeComponent();
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        var path = e.Parameter as string;
        var fullPath = Path.Combine(Core.Commons.Env.ProgramDirectory, $"LICENSES/{path}");
        if (File.Exists(fullPath))
        {
            _viewModel.License = File.ReadAllText(Path.Combine(Core.Commons.Env.ProgramDirectory, $"LICENSES/{path}"));
        }
    }
}