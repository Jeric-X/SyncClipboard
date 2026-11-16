using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.AbstractClasses;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.Desktop.Views;

public abstract class TrayIconImpl : TrayIconBase<WindowIcon>
{
    private static readonly WindowIcon _DefaultInactiveIcon = new WindowIcon(AssetLoader.Open(new Uri($"avares://SyncClipboard.Desktop/Assets/default-inactive.png")));
    private static readonly WindowIcon _ErrorInactiveIcon = new WindowIcon(AssetLoader.Open(new Uri($"avares://SyncClipboard.Desktop/Assets/erro-inactive.png")));

    protected override WindowIcon DefaultInactiveIcon => _DefaultInactiveIcon;
    protected override WindowIcon ErrorInactiveIcon => _ErrorInactiveIcon;
    protected override int MaxToolTipLenth => 1024;

    private readonly ServiceStatusViewModel _serviceStatusViewModel;
    protected override ServiceStatusViewModel? ServiceStatusViewModel => _serviceStatusViewModel;

    private readonly TrayIcon _trayIcon;
    private readonly DispatcherTimer _animationTimer = new() { Interval = AnimationInterval };

    public TrayIconImpl(ServiceStatusViewModel serviceStatusViewModel)
    {
        var icons = TrayIcon.GetIcons(App.Current);
        var trayIcon = icons?[0];
        ArgumentNullException.ThrowIfNull(trayIcon, nameof(trayIcon));
        _trayIcon = trayIcon;
        _animationTimer.Tick += (_, _) => ShowNextFrame();
        _trayIcon.Command = new RelayCommand(OnRawLeftClicked);
        _trayIcon.ToolTipText = string.Empty;
        _serviceStatusViewModel = serviceStatusViewModel;
    }

    public override void Create()
    {
    }

    protected override void PostToUIThread(Action action) => Dispatcher.UIThread.Post(action, DispatcherPriority.Send);
    protected override void StartAnimationTimer() => _animationTimer.Start();
    protected override void StopAnimationTimer() => _animationTimer.Stop();

    protected override void SetIcon(WindowIcon icon)
    {
        if (OperatingSystem.IsMacOS())
        {
            _trayIcon.SetValue(MacOSProperties.IsTemplateIconProperty, icon != DefaultInactiveIcon && icon != ErrorInactiveIcon);
        }
        _trayIcon.Icon = icon;
    }

    protected override void SetToolTip(string text) => _trayIcon.ToolTipText = text;
}
