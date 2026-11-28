using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class ServerConfigPage : UserControl
{
    private readonly ServerConfigViewModel _viewModel;

    public ServerConfigPage()
    {
        InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<ServerConfigViewModel>();
        this.DataContext = _viewModel;
    }

    private async Task<string?> GetFileByPicker(Button button, IEnumerable<string> types)
    {
        button.IsEnabled = false;
        using ScopeGuard scopeGuard = new(() => button.IsEnabled = true);

        var filePickerOpenOption = new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new(string.Join(',', types.Select(type => type[1..]))) { Patterns = types.ToList() }, new("All") { Patterns = ["*"] }]
        };

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(filePickerOpenOption);
        if (files is null || !files.Any())
        {
            return null;
        }

        return files[0].Path.LocalPath;
    }

    private async void SetCertificatePemPath(object sender, Avalonia.Interactivity.RoutedEventArgs _)
    {
        var fileName = await GetFileByPicker((Button)sender, ServerConfigViewModel.CertificatePemFileTypes);
        _viewModel.CertificatePemPath = fileName ?? _viewModel.CertificatePemPath;
    }

    private async void SetCertificatePemKeyPath(object sender, Avalonia.Interactivity.RoutedEventArgs _)
    {
        var fileName = await GetFileByPicker((Button)sender, ServerConfigViewModel.CertificatePemKeyFileTypes);
        _viewModel.CertificatePemKeyPath = fileName ?? _viewModel.CertificatePemKeyPath;
    }

    private async void SetCustomConfigurationFilePath(object sender, Avalonia.Interactivity.RoutedEventArgs _)
    {
        var fileName = await GetFileByPicker((Button)sender, ServerConfigViewModel.CustomConfigurationFileTypes);
        _viewModel.CustomConfigurationFilePath = fileName ?? _viewModel.CustomConfigurationFilePath;
    }
}
