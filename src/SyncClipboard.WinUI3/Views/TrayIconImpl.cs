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
    public override event Action? MainWindowWakedUp;

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
    protected override int MaxToolTipLenth => 255;

    public TrayIconImpl(TrayIcon trayIcon, ServiceStatusViewModel serviceStatusViewModel)
    {
        _trayIcon = trayIcon;
        _trayIcon.DoubleClickCommand = new RelayCommand(() => MainWindowWakedUp?.Invoke());
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
        return Enumerable.Range(1, 17)
            .Select(x => $"ms-appx:///Assets/upload{x:d3}.ico")
            .Select(x => new BitmapImage(new Uri(x)))
            .ToArray();
    }

    protected override BitmapImage[] DownloadIcons()
    {
        return Enumerable.Range(1, 17)
           .Select(x => $"ms-appx:///Assets/download{x:d3}.ico")
           .Select(x => new BitmapImage(new Uri(x)))
           .ToArray();
    }

    protected override void SetToolTip(string text)
    {
        _trayIcon.DispatcherQueue.EnqueueAsync(() => _trayIcon.ToolTipText = text).Wait();
    }
}
