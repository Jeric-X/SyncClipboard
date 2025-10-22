using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ViewModels;

internal partial class DiagnoseViewModel : ObservableObject
{
    public ObservableCollection<string> ClipboardTypes { get; } = [];

    private ProgramConfig _config;
    [ObservableProperty]
    private bool autoRefresh;
    partial void OnAutoRefreshChanged(bool value) => _configManager.SetConfig(_config with { DiagnosePageAutoRefresh = value });

    private readonly IClipboardChangingListener _clipboardListener;
    private readonly ConfigManager _configManager;
    private readonly SingletonTask refreshTask;
    private readonly MultiSourceClipboardReader Clipboard;

    public DiagnoseViewModel()
    {
        refreshTask = new(RefreshClipboardType);

        _clipboardListener = App.Current.Services.GetRequiredService<IClipboardChangingListener>();
        _configManager = App.Current.Services.GetRequiredService<ConfigManager>();
        Clipboard = App.Current.Services.GetRequiredService<MultiSourceClipboardReader>();
        _configManager.ListenConfig<ProgramConfig>(AotuRefreshChanged);
        _config = _configManager.GetConfig<ProgramConfig>();

        RefreshCommand.Execute(null);
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
        await Dispatcher.UIThread.InvokeAsync(() => RefreshCommand.Execute(null));
    }

    [RelayCommand]
    public void Refresh()
    {
        var _ = refreshTask.Run();
    }

    private async Task RefreshClipboardType(CancellationToken token)
    {
        ClipboardTypes.Clear();
        var types = await Clipboard.GetFormatsAsync(token);
        foreach (var item in types ?? [])
        {
            var str = item;
            try
            {
                var contentObj = await Clipboard.GetDataAsync(item, token);
                if (contentObj is not null)
                {
                    str += Environment.NewLine + contentObj?.GetType().FullName ?? string.Empty;
                }
            }
            catch (Exception ex) when (token.IsCancellationRequested is false)
            {
                str += Environment.NewLine + ex.Message;
            }
            ClipboardTypes.Add(str);
        }
    }
}
