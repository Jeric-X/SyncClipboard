using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.ViewModels;
using System.Diagnostics;
using System.Threading;

namespace SyncClipboard.Desktop.Views;

public partial class SystemSettingPage : UserControl
{
    public SystemSettingPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.GetRequiredService<SystemSettingViewModel>();
    }

    private void ExitApp(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        App.Current.ExitApp();
    }

    private void CopyAppDataFolderPath(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var profile = new TextProfile(Core.Commons.Env.AppDataDirectory);
        _ = profile.SetLocalClipboard(true, CancellationToken.None);
    }

    private void OpenDataFolderInNautilus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            Process.Start("nautilus", Core.Commons.Env.AppDataDirectory);
        }
        catch
        {
            App.Current.Logger.Write("Open Nautilus failed");
        }
    }

    private void ShowProxySettingDialog(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new ProxySettingDialog();
        dialog.ShowAsync();
    }
}
