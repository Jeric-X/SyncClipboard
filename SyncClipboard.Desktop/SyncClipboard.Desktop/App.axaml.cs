using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core;
using SyncClipboard.Desktop.ViewModels;
using SyncClipboard.Desktop.Views;
using System;

namespace SyncClipboard.Desktop;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; }
    public Window? MainWindow { get; private set; }
    private ProgramWorkflow? ProgramWorkflow;
    public new static App Current => (App)Application.Current!;
    private IClassicDesktopStyleApplicationLifetime appLife;

    public App()
    {
        Services = AppServices.ConfigureServices().BuildServiceProvider();
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
            MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
            desktop.MainWindow = MainWindow;
            appLife = desktop;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        //ProgramWorkflow = new ProgramWorkflow(Services);
        //ProgramWorkflow.Run();
        base.OnFrameworkInitializationCompleted();
    }
}
