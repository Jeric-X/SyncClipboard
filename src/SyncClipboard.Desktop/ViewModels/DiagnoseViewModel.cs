using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ViewModels;

internal partial class DiagnoseViewModel : ObservableObject
{
    public ObservableCollection<string> ClipboardTypes { get; } = new();

    public DiagnoseViewModel()
    {
        RefreshCommand.ExecuteAsync(this);
    }

    [RelayCommand]
    public async Task Refresh()
    {
        ClipboardTypes.Clear();
        var types = await App.Current.Clipboard.GetFormatsAsync();
        foreach (var item in types)
        {
            ClipboardTypes.Add(item);
        }
    }
}
