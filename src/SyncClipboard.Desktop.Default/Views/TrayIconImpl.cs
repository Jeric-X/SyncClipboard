using Avalonia.Controls;
using Avalonia.Platform;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Default.Views;

public class TrayIconImpl : Desktop.Views.TrayIconImpl
{
    private const string ResPath = "avares://SyncClipboard.Desktop.Default/Assets";
    private static readonly WindowIcon _DefaultIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/default.png")));
    private static readonly WindowIcon _ErrorIcon = new WindowIcon(AssetLoader.Open(new Uri($"{ResPath}/erro.png")));

    protected override WindowIcon DefaultIcon => _DefaultIcon;
    protected override WindowIcon ErrorIcon => _ErrorIcon;

    public TrayIconImpl(ServiceStatusViewModel serviceStatusViewModel) : base(serviceStatusViewModel)
    {
    }

    protected override WindowIcon[] UploadIcons()
    {
        return Enumerable.Range(1, 17)
            .Select(x => $"{ResPath}/upload{x:d3}.png")
            .Select(x => new WindowIcon(AssetLoader.Open(new Uri(x))))
            .ToArray();
    }

    protected override WindowIcon[] DownloadIcons()
    {
        return Enumerable.Range(1, 17)
            .Select(x => $"{ResPath}/download{x:d3}.png")
            .Select(x => new WindowIcon(AssetLoader.Open(new Uri(x))))
            .ToArray();
    }
}
