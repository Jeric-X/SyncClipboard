using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly ClipboardReaderSelector Clipboard;

    private bool _isListening;

    public DiagnoseViewModel(
        IClipboardChangingListener clipboardListener, ConfigManager configManager, ClipboardReaderSelector clipboard)
    {
        refreshTask = new(RefreshClipboardType);

        _clipboardListener = clipboardListener;
        _configManager = configManager;
        Clipboard = clipboard;
        _configManager.ListenConfig<ProgramConfig>(OnProgramConfigChanged);
        _config = _configManager.GetConfig<ProgramConfig>();

        OnProgramConfigChanged(_config);
    }

    private void OnProgramConfigChanged(ProgramConfig config)
    {
        _config = config;
        AutoRefresh = _config.DiagnosePageAutoRefresh;
        var shouldListen = config.DiagnoseMode && config.DiagnosePageAutoRefresh;
        if (_isListening == shouldListen) return;

        _isListening = shouldListen;
        if (shouldListen)
            _clipboardListener.Changed += ClipboardChangedHandler;
        else
            _clipboardListener.Changed -= ClipboardChangedHandler;
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
