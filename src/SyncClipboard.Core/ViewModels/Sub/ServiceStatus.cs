using CommunityToolkit.Mvvm.ComponentModel;

namespace SyncClipboard.Core.ViewModels.Sub;

public partial class ServiceStatus : ObservableObject
{
    [ObservableProperty]
    private bool isError = false;

    [ObservableProperty]
    private string statusString = "";

    [ObservableProperty]
    private string name = "";
}
