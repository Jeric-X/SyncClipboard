using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ViewModels;

internal partial class DiagnoseViewModel : ObservableObject
{
    public ObservableCollection<string> ClipboardTypes { get; } = new();

    private ProgramConfig _config;
    [ObservableProperty]
    private bool autoRefresh;
    partial void OnAutoRefreshChanged(bool value) => _configManager.SetConfig(_config with { DiagnosePageAutoRefresh = value });

    private readonly IClipboardChangingListener _clipboardListener;
    private readonly ConfigManager _configManager;

    public DiagnoseViewModel()
    {
        RefreshCommand.ExecuteAsync(null);

        _clipboardListener = App.Current.Services.GetRequiredService<IClipboardChangingListener>();
        _configManager = App.Current.Services.GetRequiredService<ConfigManager>();
        _configManager.ListenConfig<ProgramConfig>(AotuRefreshChanged);
        _config = _configManager.GetConfig<ProgramConfig>();
        AotuRefreshChanged(_config);
    }

    private void AotuRefreshChanged(ProgramConfig config)
    {
        _config = config;
        AutoRefresh = _config.DiagnosePageAutoRefresh;
        _clipboardListener.Changed += ClipboardChangedHandler;
        if ((config.DiagnoseMode && config.DiagnosePageAutoRefresh) is false)
        {
            _clipboardListener.Changed -= ClipboardChangedHandler;
        }
    }

    private async void ClipboardChangedHandler(ClipboardMetaInfomation _1, Profile _2)
    {
        await Dispatcher.UIThread.InvokeAsync(() => RefreshCommand.ExecuteAsync(null));
    }

    [RelayCommand]
    public async Task Refresh()
    {
        ClipboardTypes.Clear();
        var types = await App.Current.Clipboard.GetFormatsAsync();
        foreach (var item in types ?? System.Array.Empty<string>())
        {
            ClipboardTypes.Add(item);
        }
    }
}
