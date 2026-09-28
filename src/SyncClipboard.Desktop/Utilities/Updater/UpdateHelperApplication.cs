using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities.Updater.Strategies;
using SyncClipboard.Core.Utilities.Updater;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace SyncClipboard.Desktop.Utilities.Updater;

public sealed class UpdateHelperApplication(UpdateInstallTask update) : Application
{
    public static int Run(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsLinux() || args.Length != 2) throw new ArgumentException("Expected --install-update <task.json>.");
            var task = AppImageInstallWorker.Load(args[1]);
            if (Environment.GetEnvironmentVariable("APPIMAGE") != task.HelperExecutable)
                throw new InvalidOperationException("The updater must run from its private AppImage copy.");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(task.Language);
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
            return AppBuilder.Configure(() => new UpdateHelperApplication(task)).UsePlatformDetect()
                .With(new FontManagerOptions
                {
                    DefaultFamilyName = "avares://SyncClipboard.Desktop.Default/Assets/Fonts#MiSans"
                })
                .StartWithClassicDesktopLifetime([]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal Window CreateWindow()
    {
        var status = new TextBlock { Text = Strings.PreparingUpdateInstall, FontSize = 18 };
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, IsIndeterminate = true, Height = 12 };
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxHeight = 180 };
        var openLogs = new Button { Content = Strings.OpenUpdateLogs, IsVisible = false };
        openLogs.Click += (_, _) =>
        {
            try
            {
                var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                start.ArgumentList.Add(update.Directory);
                Process.Start(start)?.Dispose();
            }
            catch (Exception ex) { details.Text = ex.Message + "\n" + update.Directory; }
        };
        var window = new Window
        {
            Title = "SyncClipboard — " + Strings.InstallUpdate,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children = { status, progressBar, details, openLogs }
            }
        };
        var cancellation = new CancellationTokenSource();
        var running = false;
        var closing = false;
        window.Closing += (_, e) =>
        {
            if (!running) return;
            closing = true;
            cancellation.Cancel();
            e.Cancel = true; // Allow a normal close to finish rollback; force termination is not recoverable here.
        };
        window.Closed += (_, _) => cancellation.Dispose();
        window.Opened += async (_, _) =>
        {
            running = true;
            try
            {
                var progress = new Progress<UpdateInstallProgress>(value =>
                {
                    status.Text = GetPhaseText(value.Phase);
                    progressBar.IsIndeterminate = !value.Percent.HasValue;
                    progressBar.Value = value.Percent ?? 0;
                    details.Text = value.Percent.HasValue ? $"{value.Percent:0}%" : string.Empty;
                });
                var success = await Task.Run(() => new AppImageInstallWorker(update).RunAsync(progress, cancellation.Token));
                running = false;
                if (success || closing) window.Close();
                else
                {
                    status.Text = Strings.UpdateInstallFailed;
                    progressBar.IsIndeterminate = false;
                    details.Text = await File.ReadAllTextAsync(Path.Combine(update.Directory, "failed"));
                    openLogs.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                running = false;
                status.Text = Strings.UpdateInstallFailed;
                details.Text = ex.Message + "\n" + update.Directory;
                openLogs.IsVisible = true;
            }
        };
        return window;
    }

    internal static string GetPhaseText(string phase) => phase switch
    {
        "backup" => Strings.UpdateBackingUp,
        "installing" => Strings.InstallingUpdate,
        "restoring" => Strings.UpdateRestoring,
        "waiting" => Strings.UpdateWaitingForExit,
        "verifying" => Strings.UpdateVerifying,
        "starting" => Strings.UpdateRestarting,
        "failed" => Strings.UpdateInstallFailed,
        _ => Strings.PreparingUpdateInstall
    };
}
