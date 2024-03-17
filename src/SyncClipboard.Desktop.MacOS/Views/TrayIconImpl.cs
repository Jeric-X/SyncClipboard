using AppKit;
using Avalonia.Controls;
using Avalonia.Platform;
using Foundation;
using SyncClipboard.Core.ViewModels;
using System;
using System.Linq;

namespace SyncClipboard.Desktop.MacOS.Views;

internal class TrayIconImpl : Desktop.Views.TrayIconImpl
{
    private const string ResPath = "avares://SyncClipboard.Desktop.MacOS/Assets";
    private static readonly WindowIcon _lightDefaultIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/Light/default.png")));
    private static readonly WindowIcon _lightErrorIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/Light/erro.png")));
    private static readonly WindowIcon _darkDefaultIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/Dark/default.png")));
    private static readonly WindowIcon _darkErrorIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/Dark/erro.png")));

    protected override WindowIcon DefaultIcon => IsLightMode ? _lightDefaultIcon : _darkDefaultIcon;
    protected override WindowIcon ErrorIcon => IsLightMode ? _lightErrorIcon : _darkErrorIcon;

    private bool IsLightMode = true;

    public TrayIconImpl(ServiceStatusViewModel serviceStatusViewModel) : base(serviceStatusViewModel)
    {
        NSApplication.SharedApplication.AddObserver(
            "effectiveAppearance",
            NSKeyValueObservingOptions.New,
            _ => SystemThemeChanged()
        );
        RefreshIcon();
    }

    private void SystemThemeChanged()
    {
        bool isLightMode;
        if (NSApplication.SharedApplication.EffectiveAppearance.Name.Contains("dark", StringComparison.OrdinalIgnoreCase))
            isLightMode = false;
        else
            isLightMode = true;

        if (isLightMode != IsLightMode)
        {
            IsLightMode = isLightMode;
            RefreshIcon();
        }
    }

    protected override WindowIcon[] UploadIcons()
    {
        string path = $"{ResPath}/{(IsLightMode ? "Light" : "Dark")}";
        return Enumerable.Range(1, 17)
            .Select(x => $"{path}/upload{x:d3}.png")
            .Select(x => new WindowIcon(AssetLoader.Open(new Uri(x))))
            .ToArray();
    }

    protected override WindowIcon[] DownloadIcons()
    {
        string path = $"{ResPath}/{(IsLightMode ? "Light" : "Dark")}";
        return Enumerable.Range(1, 17)
            .Select(x => $"{path}/download{x:d3}.png")
            .Select(x => new WindowIcon(AssetLoader.Open(new Uri(x))))
            .ToArray();
    }
}
