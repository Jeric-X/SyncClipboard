using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SyncClipboard.Core;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Desktop.Views;
using System;
using MenuItem = SyncClipboard.Core.Interfaces.MenuItem;

namespace SyncClipboard.Desktop;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; }
    public new static App Current => (App)Application.Current!;
    public MainWindow MainWindow => (MainWindow)Services.GetRequiredService<IMainWindow>();
    public IClipboard Clipboard => MainWindow.Clipboard!;

    private IClassicDesktopStyleApplicationLifetime _appLife;
    private ProgramWorkflow? _programWorkflow;

#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
    public App()
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
    {
        Services = AppServices.ConfigureServices().BuildServiceProvider();
    }

#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
    public App(ServiceCollection serviceCollection)
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
    {
        Services = serviceCollection.BuildServiceProvider();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _appLife = desktop;
        }
        else
        {
            throw new NotImplementedException("Not support this system.");
        }
        base.OnFrameworkInitializationCompleted();

        _programWorkflow = new ProgramWorkflow(Services);
        _programWorkflow.Run();
    }

    public void ExitApp()
    {
        _programWorkflow?.Stop();
        _appLife.Shutdown();
    }

    private void NativeMenuItem_Click(object? sender, System.EventArgs e)
    {
        ExitApp();
    }
}
