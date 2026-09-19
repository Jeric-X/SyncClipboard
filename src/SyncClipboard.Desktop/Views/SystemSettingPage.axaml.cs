using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SyncClipboard.Desktop.Views;

public partial class SystemSettingPage : UserControl
{
    private Window? _permissionStatusWindow;

    public SystemSettingPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.GetRequiredService<SystemSettingViewModel>();
        Loaded += (_, _) =>
        {
            RefreshInputPermissions();
            _permissionStatusWindow = TopLevel.GetTopLevel(this) as Window;
            if (_permissionStatusWindow is not null) _permissionStatusWindow.Activated += OnPermissionStatusWindowActivated;
        };
        Unloaded += (_, _) =>
        {
            if (_permissionStatusWindow is not null) _permissionStatusWindow.Activated -= OnPermissionStatusWindowActivated;
            _permissionStatusWindow = null;
        };
    }

    private void OnPermissionStatusWindowActivated(object? sender, EventArgs e) => RefreshInputPermissions();

    private void RefreshInputPermissions() => (DataContext as SystemSettingViewModel)?.RefreshInputPermissions();

    public static List<KeyValuePair<string, Action>> Operations { get; } = GetOperations();

    private static List<KeyValuePair<string, Action>> GetOperations()
    {
        List<KeyValuePair<string, Action>> operations = [
            new (Strings.CompletelyExit, AppCore.Current.ExitApp),
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
        var setter = App.Current.Services.GetRequiredService<LocalClipboardSetter>();
        _ = setter.Set(profile, CancellationToken.None);

        var helper = App.Current.Services.GetRequiredService<ProfileNotificationHelper>();
        helper.Notify(profile);
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

    private async void ChangeAppDataFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.AppDataFolder,
            AllowMultiple = false
        });

        if (folders is null || folders.Count == 0) return;

        var selectedFolder = folders[0].Path.LocalPath;
        var viewModel = (SystemSettingViewModel)DataContext!;

        if (sender is Button button) button.IsEnabled = false;
        try
        {
            await viewModel.ChangeAppDataFolderAsync(selectedFolder);
        }
        finally
        {
            if (sender is Button btn) btn.IsEnabled = true;
        }
    }
}
