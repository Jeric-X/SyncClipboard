using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
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
        Height = _viewModel.Height;
        Width = _viewModel.Width;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _viewModel.Height = (int)Height;
        _viewModel.Width = (int)Width;
        if (e.CloseReason == WindowCloseReason.ApplicationShutdown || e.CloseReason == WindowCloseReason.OSShutdown)
        {
            base.OnClosing(e);
            return;
        }
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

    public void OpenPage(PageDefinition page, object? para)
    {
        void action()
        {
            this.ShowMainWindow();
            var index = _viewModel.MainWindowPage.IndexOf(page);
            if (index != -1)
            {
                _MainView._MenuList.SelectedIndex = _viewModel.MainWindowPage.IndexOf(page);
            }
        }
        RunOnMainThread(action);
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
        this.Activate();
    }

    private void RunOnMainThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(action);
        }
    }

    void IMainWindow.Show()
    {
        RunOnMainThread(ShowMainWindow);
    }

    [RelayCommand]
    private void CloseWindow() => this.Close();

    [RelayCommand]
    protected virtual void MinimizeWindow() => this.Close();

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
            e.Handled = true;
        }
    }
}
