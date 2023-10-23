using CommunityToolkit.Mvvm.ComponentModel;

namespace SyncClipboard.Core.ViewModels;

public partial class LicenseViewModel : ObservableObject
{
    [ObservableProperty]
    private string license = "";
}
