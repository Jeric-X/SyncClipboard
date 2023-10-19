using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public string[]? clipboards;

    [RelayCommand]
    public async Task Reresh()
    {
        IClipboard Clipboard = ((Window)App.Current.Services.GetRequiredService<IMainWindow>()).Clipboard!;
        Clipboards = await Clipboard.GetFormatsAsync();
    }
}
