using Avalonia.Controls;
using Avalonia.Platform;
using SyncClipboard.Core.ViewModels;
using System;
using System.Collections.Generic;

namespace SyncClipboard.Desktop.MacOS.Views;

internal class TrayIconImpl(ServiceStatusViewModel serviceStatusViewModel) : Desktop.Views.TrayIconImpl(serviceStatusViewModel)
{
    private const string ResPath = "avares://SyncClipboard.Desktop.MacOS/Assets";
    private static string ThemePath => $"{ResPath}/Light";
    private static readonly WindowIcon _lightDefaultIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/Light/default.png")));
    private static readonly WindowIcon _lightErrorIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/Light/erro.png")));
    private static readonly WindowIcon _defaultInactiveIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/default-inactive.png")));
    private static readonly WindowIcon _errorInactiveIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/erro-inactive.png")));

    protected override WindowIcon DefaultInactiveIcon => _defaultInactiveIcon;
    protected override WindowIcon ErrorInactiveIcon => _errorInactiveIcon;

    protected override WindowIcon DefaultIcon => _lightDefaultIcon;
    protected override WindowIcon ErrorIcon => _lightErrorIcon;

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
}
