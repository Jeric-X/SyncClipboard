using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml.Media.Imaging;
using SyncClipboard.Core.AbstractClasses;
using SyncClipboard.Core.ViewModels;
using System;
using System.Linq;

namespace SyncClipboard.WinUI3.Views;

internal class TrayIconImpl : TrayIconBase<BitmapImage>
{
    private readonly TrayIcon _trayIcon;
    private readonly ServiceStatusViewModel _serviceStatusViewModel;
    protected override ServiceStatusViewModel? ServiceStatusViewModel => _serviceStatusViewModel;

    private readonly BitmapImage defaultIcon = new BitmapImage(new Uri("ms-appx:///Assets/default.ico"));
    private readonly BitmapImage defaultInactiveIcon = new BitmapImage(new Uri("ms-appx:///Assets/default-inactive.ico"));
    private readonly BitmapImage errorIcon = new BitmapImage(new Uri("ms-appx:///Assets/erro.ico"));
    private readonly BitmapImage errorInactiveIcon = new BitmapImage(new Uri("ms-appx:///Assets/erro-inactive.ico"));
    protected override BitmapImage DefaultIcon => defaultIcon;
    protected override BitmapImage DefaultInactiveIcon => defaultInactiveIcon;
    protected override BitmapImage ErrorIcon => errorIcon;
    protected override BitmapImage ErrorInactiveIcon => errorInactiveIcon;
    protected override int MaxToolTipLenth => 1024;

    public TrayIconImpl(TrayIcon trayIcon, ServiceStatusViewModel serviceStatusViewModel)
    {
        _trayIcon = trayIcon;
        _trayIcon.NoLeftClickDelay = true;
        _trayIcon.LeftClickCommand = new RelayCommand(OnRawLeftClicked);
        _trayIcon.DoubleClickCommand = new RelayCommand(OnRawLeftClicked);
        _serviceStatusViewModel = serviceStatusViewModel;
    }

    public override void Create()
    {
        try
        {
            _trayIcon.ForceCreate();
        }
        catch (Exception ex)
        {
            App.Current.Logger.Write("Set Efficiency Mode failed with exception: " + ex.Message);
            _trayIcon.ForceCreate(false);
        }
    }

    protected override void SetIcon(BitmapImage icon)
    {
        if (_trayIcon.DispatcherQueue.HasThreadAccess)
        {
            _trayIcon.IconSource = icon;
        }
        else
        {
            _trayIcon.DispatcherQueue.EnqueueAsync(() => _trayIcon.IconSource = icon);
        }
    }

    protected override BitmapImage[] UploadIcons()
    {
        if (_trayIcon.DispatcherQueue.HasThreadAccess)
        {
            return CreateUploadIcons();
        }
        BitmapImage[]? icons = null;
        _trayIcon.DispatcherQueue.EnqueueAsync(() => icons = CreateUploadIcons()).Wait();
        return icons!;
    }

    private BitmapImage[] CreateUploadIcons()
    {
        return Enumerable.Range(1, 17)
            .Select(x => $"ms-appx:///Assets/upload{x:d3}.ico")
            .Select(x => new BitmapImage(new Uri(x)))
            .ToArray();
    }

    protected override BitmapImage[] DownloadIcons()
    {
        if (_trayIcon.DispatcherQueue.HasThreadAccess)
        {
            return CreateDownloadIcons();
        }
        BitmapImage[]? icons = null;
        _trayIcon.DispatcherQueue.EnqueueAsync(() => icons = CreateDownloadIcons()).Wait();
        return icons!;
    }

    private BitmapImage[] CreateDownloadIcons()
    {
        return Enumerable.Range(1, 17)
            .Select(x => $"ms-appx:///Assets/download{x:d3}.ico")
            .Select(x => new BitmapImage(new Uri(x)))
            .ToArray();
    }

    protected override void SetToolTip(string text)
    {
        if (_trayIcon.DispatcherQueue.HasThreadAccess)
        {
            _trayIcon.ToolTipText = text;
        }
        else
        {
            _ = _trayIcon.DispatcherQueue.EnqueueAsync(() => _trayIcon.ToolTipText = text);
        }
    }
}
