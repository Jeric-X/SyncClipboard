using CommunityToolkit.Mvvm.ComponentModel;

namespace SyncClipboard.Core.ViewModels.Sub;

public partial class ServiceStatus : ObservableObject
{
    [ObservableProperty]
    public partial bool IsError { get; set; } = false;

    [ObservableProperty]
    public partial string StatusString { get; set; } = "";

    [ObservableProperty]
    public partial string Name { get; set; } = "";
}
