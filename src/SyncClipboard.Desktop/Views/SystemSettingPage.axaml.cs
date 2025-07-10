using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SyncClipboard.Desktop.Views;

public partial class SystemSettingPage : UserControl
{
    public SystemSettingPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.GetRequiredService<SystemSettingViewModel>();
    }

    public static List<KeyValuePair<string, Action>> Operations { get; } = GetOperations();

    private static List<KeyValuePair<string, Action>> GetOperations()
    {
        List<KeyValuePair<string, Action>> operations = [
            new (Strings.CompletelyExit, App.Current.ExitApp),
            new (Strings.CopyAppDataFolderPath, CopyAppDataFolderPath),
            new (Strings.OpenDataFolderInNautilus, OpenDataFolderInNautilus),
        ];

        if (OperatingSystem.IsLinux() && Core.Commons.Env.GetAppImageExecPath() != null)
        {
            operations.AddRange([
                new (Strings.AddAppImageToUserAppLauncher, AddAppImageToUserAppLauncher),
                new (Strings.RemoveAppImageFromUserAppLauncher, RemoveAppImageFromUserAppLauncher)
            ]);
        }

#if DEBUG
        operations.AddRange([
                new (Strings.AddAppImageToUserAppLauncher, AddAppImageToUserAppLauncher),
                new (Strings.RemoveAppImageFromUserAppLauncher, RemoveAppImageFromUserAppLauncher)
            ]);
#endif
        return operations;
    }

    [RelayCommand]
    private void RunOperation(Action operation)
    {
        operation.Invoke();
    }

    private static void AddAppImageToUserAppLauncher()
    {
        DesktopEntryHelper.SetLinuxDesktopEntry(Core.Commons.Env.LinuxUserDesktopEntryFolder);
    }

    private static void RemoveAppImageFromUserAppLauncher()
    {
        DesktopEntryHelper.RemvoeLinuxDesktopEntry(Core.Commons.Env.LinuxUserDesktopEntryFolder);
    }

    private static void CopyAppDataFolderPath()
    {
        var profile = new TextProfile(Core.Commons.Env.AppDataDirectory);
        _ = profile.SetLocalClipboard(true, CancellationToken.None);
    }

    private static void OpenDataFolderInNautilus()
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
