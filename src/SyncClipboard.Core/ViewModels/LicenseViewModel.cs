using CommunityToolkit.Mvvm.ComponentModel;

namespace SyncClipboard.Core.ViewModels;

public partial class LicenseViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string License { get; set; } = "";
}
