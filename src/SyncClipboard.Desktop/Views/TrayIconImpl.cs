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
    protected override int MaxToolTipLenth => 255;

    private readonly ServiceStatusViewModel _serviceStatusViewModel;
    protected override ServiceStatusViewModel? ServiceStatusViewModel => _serviceStatusViewModel;

    public override event Action? MainWindowWakedUp;

    private readonly TrayIcon _trayIcon;

    public TrayIconImpl(ServiceStatusViewModel serviceStatusViewModel)
    {
        var icons = TrayIcon.GetIcons(App.Current);
        var trayIcon = icons?[0];
        ArgumentNullException.ThrowIfNull(trayIcon, nameof(trayIcon));
        _trayIcon = trayIcon;
        _trayIcon.Command = new RelayCommand(() => MainWindowWakedUp?.Invoke());
        _trayIcon.ToolTipText = string.Empty;
        _serviceStatusViewModel = serviceStatusViewModel;
    }

    public override void Create()
    {
    }

    protected override void SetIcon(WindowIcon icon)
    {
        ActionInUIThread(() =>
        {
            if (OperatingSystem.IsMacOS())
            {
                if (icon == DefaultInactiveIcon || icon == ErrorInactiveIcon)
                {
                    _trayIcon.SetValue(MacOSProperties.IsTemplateIconProperty, false);
                }
                else
                {
                    _trayIcon.SetValue(MacOSProperties.IsTemplateIconProperty, true);
                }
            }
            _trayIcon.Icon = icon;
        });
    }

    protected override void SetToolTip(string text)
    {
        ActionInUIThread(() => _trayIcon.ToolTipText = text);
    }

    private static void ActionInUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action.Invoke();
        }
        else
        {
            Dispatcher.UIThread.Post(action, DispatcherPriority.Send);
        }
    }
}
