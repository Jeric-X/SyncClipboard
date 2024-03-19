using AppKit;
using Avalonia.Controls;
using Avalonia.Platform;
using Foundation;
using SyncClipboard.Core.ViewModels;
using System;
using System.Collections.Generic;

namespace SyncClipboard.Desktop.MacOS.Views;

internal class TrayIconImpl : Desktop.Views.TrayIconImpl, IDisposable
{
    private const string ResPath = "avares://SyncClipboard.Desktop.MacOS/Assets";
    private string ThemePath => $"{ResPath}/{(_isLightMode ? "Light" : "Dark")}";
    private static readonly WindowIcon _lightDefaultIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/Light/default.png")));
    private static readonly WindowIcon _lightErrorIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/Light/erro.png")));
    private static readonly WindowIcon _darkDefaultIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/Dark/default.png")));
    private static readonly WindowIcon _darkErrorIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/Dark/erro.png")));

    protected override WindowIcon DefaultIcon => _isLightMode ? _lightDefaultIcon : _darkDefaultIcon;
    protected override WindowIcon ErrorIcon => _isLightMode ? _lightErrorIcon : _darkErrorIcon;

    private bool _isLightMode = true;
    private readonly IDisposable _observer;

    public TrayIconImpl(ServiceStatusViewModel serviceStatusViewModel) : base(serviceStatusViewModel)
    {
        _observer = NSApplication.SharedApplication.AddObserver(
            "effectiveAppearance",
            NSKeyValueObservingOptions.New,
            SystemThemeChanged
        );
        _isLightMode = GetIsLightMode();
        RefreshIcon();
    }

    private void SystemThemeChanged(NSObservedChange observedChange)
    {
        bool isLightMode = GetIsLightMode();

        if (isLightMode != _isLightMode)
        {
            _isLightMode = isLightMode;
            RefreshIcon();
        }
    }

    private static bool GetIsLightMode()
    {
        if (NSApplication.SharedApplication.EffectiveAppearance.Name.Contains("dark", StringComparison.OrdinalIgnoreCase))
            return false;
        else
            return true;
    }

    protected override IEnumerable<WindowIcon> UploadIcons()
    {
        for (int i = 1; i <= 17; i++)
        {
            var path = $"{ThemePath}/upload{i:d3}.png";
            yield return new WindowIcon(AssetLoader.Open(new Uri(path)));
        }
    }

    protected override IEnumerable<WindowIcon> DownloadIcons()
    {
        for (int i = 1; i <= 17; i++)
        {
            var path = $"{ThemePath}/download{i:d3}.png";
            yield return new WindowIcon(AssetLoader.Open(new Uri(path)));
        }
    }

    ~TrayIconImpl() => Dispose();

    public void Dispose()
    {
        try
        {
            NSApplication.SharedApplication.RemoveObserver((NSObject)_observer, "effectiveAppearance");
            _observer.Dispose();
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}
