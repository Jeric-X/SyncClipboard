using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public string Greeting => "Welcome to Avalonia!";

    [ObservableProperty]
    public string[]? clipboards;

    [RelayCommand]
    public async Task Reresh()
    {
        IClipboard Clipboard = App.Current.MainWindow!.Clipboard!;
        Clipboards = await Clipboard.GetFormatsAsync();
    }
}
