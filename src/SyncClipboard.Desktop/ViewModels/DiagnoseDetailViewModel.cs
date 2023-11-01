using CommunityToolkit.Mvvm.ComponentModel;

namespace SyncClipboard.Desktop.ViewModels;

internal partial class DiagnoseDetailViewModel : ObservableObject
{
    [ObservableProperty]
    private string? text;
}
