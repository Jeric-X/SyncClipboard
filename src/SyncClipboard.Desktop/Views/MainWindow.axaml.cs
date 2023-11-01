using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.Desktop.Views;

public partial class MainWindow : Window, IMainWindow
{
    private readonly MainViewModel _viewModel;
    public MainWindow()
    {
        _viewModel = App.Current.Services.GetRequiredService<MainViewModel>();
        if (OperatingSystem.IsLinux() is false)
        {
            this.ExtendClientAreaToDecorationsHint = true;
        }
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        this.Hide();
        e.Cancel = true;
    }

    internal void NavigateTo(
        PageDefinition page,
        SlideNavigationTransitionEffect effect = SlideNavigationTransitionEffect.FromBottom,
        object? parameter = null)
    {
        _MainView.NavigateTo(page, effect, parameter);
    }

    public void NavigateTo(PageDefinition page, NavigationTransitionEffect effect, object? para = null)
    {
        SlideNavigationTransitionEffect platformEffect = effect switch
        {
            NavigationTransitionEffect.FromBottom => SlideNavigationTransitionEffect.FromBottom,
            NavigationTransitionEffect.FromLeft => SlideNavigationTransitionEffect.FromLeft,
            NavigationTransitionEffect.FromRight => SlideNavigationTransitionEffect.FromRight,
            NavigationTransitionEffect.FromTop => SlideNavigationTransitionEffect.FromTop,
            _ => throw new NotImplementedException()
        };
        _MainView.NavigateTo(page, platformEffect, para);
    }

    public void NavigateToLastLevel()
    {
        _viewModel.NavigateToLastLevel();
    }

    public void NavigateToNextLevel(PageDefinition page, object? para = null)
    {
        _viewModel.NavigateToNextLevel(page, para);
    }

    internal void DispableScrollViewer()
    {
        _MainView.DispableScrollViewer();
    }

    internal void EnableScrollViewer()
    {
        _MainView.EnableScrollViewer();
    }

    public void SetFont(string font)
    {
        if (string.IsNullOrEmpty(font))
        {
            App.Current.Resources["ProgramFont"] = App.Current.Resources["DefaultFont"];
        }
        else
        {
            App.Current.Resources["ProgramFont"] = new FontFamily(font);
        }
    }

    public void ExitApp()
    {
        App.Current.ExitApp();
    }

    protected virtual void ShowMainWindow()
    {
        this.Show();
    }

    void IMainWindow.Show()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowMainWindow();
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(new Action(() =>
            {
                ShowMainWindow();
            }));
        }
    }

    public virtual void Init(bool hide)
    {
        if (!hide)
        {
            Show();
        }
    }
}
