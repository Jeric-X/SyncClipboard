using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ViewModels;

internal partial class DiagnoseViewModel : ObservableObject
{
    public ObservableCollection<string> ClipboardTypes { get; } = new();

    public DiagnoseViewModel()
    {
        RefreshCommand.ExecuteAsync(this);
        var listener = App.Current.Services.GetRequiredService<IClipboardChangingListener>();
        listener.Changed += async (_) => await Dispatcher.UIThread.InvokeAsync(() => RefreshCommand.ExecuteAsync(null));
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
