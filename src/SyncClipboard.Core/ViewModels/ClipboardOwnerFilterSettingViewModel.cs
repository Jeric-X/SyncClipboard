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

public partial class ClipboardOwnerFilterSettingViewModel : ObservableObject
{
    public static readonly LocaleString<string>[] Modes =
    [
        new ("", Strings.None),
        new ("BlackList", Strings.BlackList),
        new ("WhiteList", Strings.WhiteList)
    ];

    [ObservableProperty]
    private LocaleString<string> filterMode = Modes[0];
    partial void OnFilterModeChanged(LocaleString<string> value)
    {
        UpdateFilterList();
        FilterConfig = FilterConfig with { FilterMode = value.Key };
    }

    [ObservableProperty]
    private ClipboardOwnerFilterConfig filterConfig = new();
    partial void OnFilterConfigChanged(ClipboardOwnerFilterConfig value)
    {
        FilterMode = Modes.FirstOrDefault(x => x.Key == FilterConfig.FilterMode) ?? Modes[0];
        UpdateFilterList();
        _configManager.SetConfig(value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private bool enableText = false;

    [ObservableProperty]
    private bool isListening = false;

    public string? Description => EnableText ? I18n.Strings.ClipboardOwnerFilterDescription : null;

    public ObservableCollection<EditableWindowInfo> FilterList { get; } = [];

    public event Action<ForegroundWindowInfo>? OnClipboardOwnerCaptured;

    private bool _isUpdating = false;

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

    public void AddItem(ForegroundWindowInfo info)
    {
        if (FilterConfig.FilterMode == "") return;
        FilterList.Add(new EditableWindowInfo(info));
        SaveToConfig();
    }

    public void UpdateItem(EditableWindowInfo item, ForegroundWindowInfo newInfo)
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
            OnClipboardOwnerCaptured?.Invoke(new ForegroundWindowInfo());
        }
    }

    private readonly ConfigManager _configManager;
    private readonly IClipboardChangingListener _clipboardChangingListener;

    public ClipboardOwnerFilterSettingViewModel(ConfigManager configManager, IClipboardChangingListener clipboardChangingListener)
    {
        _configManager = configManager;
        _clipboardChangingListener = clipboardChangingListener;
        configManager.GetAndListenConfig<ClipboardOwnerFilterConfig>(config => FilterConfig = config);
    }
}

public partial class EditableWindowInfo : ObservableObject
{
    [ObservableProperty]
    private string processName = "";

    [ObservableProperty]
    private string windowTitle = "";

    [ObservableProperty]
    private string executableName = "";

    public bool IsEmpty => string.IsNullOrWhiteSpace(ProcessName) && string.IsNullOrWhiteSpace(WindowTitle) && string.IsNullOrWhiteSpace(ExecutableName);

    public EditableWindowInfo(ForegroundWindowInfo info)
    {
        ProcessName = info.ProcessName ?? "";
        WindowTitle = info.WindowTitle ?? "";
        ExecutableName = info.ExecutableName ?? "";
    }

    public ForegroundWindowInfo ToWindowInfo() => new()
    {
        ProcessName = ProcessName,
        WindowTitle = WindowTitle,
        ExecutableName = ExecutableName
    };
}
