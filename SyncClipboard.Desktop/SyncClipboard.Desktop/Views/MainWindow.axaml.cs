using Avalonia.Controls;
using FluentAvalonia.UI.Media.Animation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.Desktop.Views;

public partial class MainWindow : Window, IMainWindow
{
    public MainWindow()
    {
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

    public void NavigateTo(PageDefinition page, NavigationTransitionEffect effect)
    {
        SlideNavigationTransitionEffect platformEffect = effect switch
        {
            NavigationTransitionEffect.FromBottom => SlideNavigationTransitionEffect.FromBottom,
            NavigationTransitionEffect.FromLeft => SlideNavigationTransitionEffect.FromLeft,
            NavigationTransitionEffect.FromRight => SlideNavigationTransitionEffect.FromRight,
            NavigationTransitionEffect.FromTop => SlideNavigationTransitionEffect.FromTop,
            _ => throw new NotImplementedException()
        };
        _MainView.NavigateTo(page, platformEffect);
    }
}
