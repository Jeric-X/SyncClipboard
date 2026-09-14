using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public partial class ClipboardOwnerFilterSettingViewModel(ConfigManager configManager, IClipboardChangingListener clipboardChangingListener) : ObservableObject
{
    public static readonly LocaleString<string>[] Modes =
    [
        new ("", Strings.None),
        new ("BlackList", Strings.BlackList),
        new ("WhiteList", Strings.WhiteList)
    ];

    [ObservableProperty]
    public partial LocaleString<string> FilterMode { get; set; } = Modes[0];

    partial void OnFilterModeChanged(LocaleString<string> value)
    {
        UpdateFilterList();
        FilterConfig = FilterConfig with { FilterMode = value.Key };
    }

    [ObservableProperty]
    public partial ClipboardOwnerFilterConfig FilterConfig { get; set; } = new();

    partial void OnFilterConfigChanged(ClipboardOwnerFilterConfig value)
    {
        FilterMode = Modes.FirstOrDefault(x => x.Key == FilterConfig.FilterMode) ?? Modes[0];
        UpdateFilterList();
        ArgumentNullException.ThrowIfNull(_configKey);
        _configManager.SetConfig(_configKey, value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    public partial bool EnableText { get; set; } = false;

    [ObservableProperty]
    public partial bool IsListening { get; set; } = false;

    public string? Description => EnableText ? Strings.ClipboardOwnerFilterDescription : null;

    public ObservableCollection<EditableWindowInfo> FilterList { get; } = [];

    public event Action<WindowInfo>? OnClipboardOwnerCaptured;

    private bool _isUpdating = false;

    private readonly HashSet<string> _listenedConfigKeys = [];
    private string? _configKey;

    public void UseConfig(string configKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configKey);

        if (_listenedConfigKeys.Add(configKey))
        {
            _configManager.ListenConfig<ClipboardOwnerFilterConfig>(configKey, config =>
            {
                if (_configKey == configKey)
                {
                    FilterConfig = config;
                }
            });
        }

        _configKey = configKey;
        LoadCurrentConfig();
    }

    private void LoadCurrentConfig()
    {
        ArgumentNullException.ThrowIfNull(_configKey);
        FilterConfig = _configManager.GetConfig<ClipboardOwnerFilterConfig>(_configKey) ?? new();
        FilterMode = Modes.FirstOrDefault(x => x.Key == FilterConfig.FilterMode) ?? Modes[0];
        UpdateFilterList();
    }

    private void UpdateFilterList()
    {
        if (_isUpdating) return;
        _isUpdating = true;

        FilterList.Clear();
        var list = FilterConfig.FilterMode == "BlackList" ? FilterConfig.BlackList :
                   FilterConfig.FilterMode == "WhiteList" ? FilterConfig.WhiteList : [];
        foreach (var item in list)
        {
            FilterList.Add(new EditableWindowInfo(item));
        }

        EnableText = FilterConfig.FilterMode != "";
        _isUpdating = false;
    }

    public void AddItem(WindowInfo info)
    {
        if (FilterConfig.FilterMode == "") return;
        FilterList.Add(new EditableWindowInfo(info));
        SaveToConfig();
    }

    public void UpdateItem(EditableWindowInfo item, WindowInfo newInfo)
    {
        item.ProcessName = newInfo.ProcessName ?? "";
        item.WindowTitle = newInfo.WindowTitle ?? "";
        item.ExecutableName = newInfo.ExecutableName ?? "";
        SaveToConfig();
    }

    public void RemoveItem(EditableWindowInfo item)
    {
        FilterList.Remove(item);
        SaveToConfig();
    }

    public void SaveToConfig()
    {
        if (_isUpdating) return;
        _isUpdating = true;

        var list = FilterList
            .Where(x => !x.IsEmpty)
            .Select(x => x.ToWindowInfo())
            .ToList();

        if (FilterConfig.FilterMode == "BlackList")
        {
            FilterConfig = FilterConfig with { BlackList = list };
        }
        else if (FilterConfig.FilterMode == "WhiteList")
        {
            FilterConfig = FilterConfig with { WhiteList = list };
        }

        _isUpdating = false;
    }

    [RelayCommand]
    public void Confirm()
    {
        SaveToConfig();
        AppCore.Current.Services.GetRequiredService<MainViewModel>().NavigateToLastLevel();
    }

    [RelayCommand]
    public void ToggleListening()
    {
        if (IsListening)
        {
            StopListening();
        }
        else
        {
            StartListening();
        }
    }

    public void StartListening()
    {
        if (IsListening) return;
        IsListening = true;
        _clipboardChangingListener.Changed += OnClipboardChanged;
    }

    public void StopListening()
    {
        if (!IsListening) return;
        IsListening = false;
        _clipboardChangingListener.Changed -= OnClipboardChanged;
    }

    private void OnClipboardChanged(ClipboardMetaInfomation meta, Profile profile)
    {
        StopListening();
        var owner = meta.Owner;
        if (owner is not null)
        {
            OnClipboardOwnerCaptured?.Invoke(owner.Value);
        }
        else
        {
            OnClipboardOwnerCaptured?.Invoke(new WindowInfo());
        }
    }

    private readonly ConfigManager _configManager = configManager;
    private readonly IClipboardChangingListener _clipboardChangingListener = clipboardChangingListener;
}

public partial class EditableWindowInfo(WindowInfo info) : ObservableObject
{
    [ObservableProperty]
    public partial string ProcessName { get; set; } = info.ProcessName ?? "";

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = info.WindowTitle ?? "";

    [ObservableProperty]
    public partial string ExecutableName { get; set; } = info.ExecutableName ?? "";

    public bool IsEmpty => string.IsNullOrWhiteSpace(ProcessName) && string.IsNullOrWhiteSpace(WindowTitle) && string.IsNullOrWhiteSpace(ExecutableName);

    public WindowInfo ToWindowInfo() => new()
    {
        ProcessName = ProcessName,
        WindowTitle = WindowTitle,
        ExecutableName = ExecutableName
    };
}