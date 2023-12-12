using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.ViewModels;
using System;
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

    private void CopyAppDataDirPath(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var profile = new TextProfile(
            Core.Commons.Env.AppDataDirectory,
            App.Current.Services.GetRequiredService<IServiceProvider>()
        );
        profile.SetLocalClipboard(true, CancellationToken.None);
    }
}
