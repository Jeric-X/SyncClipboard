using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public partial class HotkeyBlacklistViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;
    private readonly ForegroundWindowCapture _captureService;
    private readonly HotkeyManager _hotkeyManager;
    private HotkeyBlacklistConfig _config = new();
    private bool _isLoading;

    [ObservableProperty]
    private bool isEnabled;
    partial void OnIsEnabledChanged(bool value)
    {
        if (!_isLoading)
        {
            SaveConfig();
        }
    }

    [ObservableProperty]
    private bool isCapturing;

    [ObservableProperty]
    private string captureHint = string.Empty;

    [ObservableProperty]
    private bool isSystemHotkeysSuspended;

    public ObservableCollection<EditableWindowInfo> BlackList { get; } = [];

    public event Action<WindowInfo>? WindowCaptured;

    public HotkeyBlacklistViewModel(
        ConfigManager configManager,
        ForegroundWindowCapture captureService,
        HotkeyManager hotkeyManager)
    {
        _configManager = configManager;
        _captureService = captureService;
        _hotkeyManager = hotkeyManager;

        _captureService.WindowCaptured += OnWindowCaptured;
        _hotkeyManager.HotkeyStatusChanged += UpdateSuspendedStatus;
        _configManager.GetAndListenConfig<HotkeyBlacklistConfig>(LoadConfig);
        UpdateSuspendedStatus();
    }

    private void LoadConfig(HotkeyBlacklistConfig config)
    {
        _isLoading = true;
        _config = config;
        IsEnabled = config.Enabled;
        BlackList.Clear();
        foreach (var item in config.BlackList)
        {
            BlackList.Add(new EditableWindowInfo(item));
        }
        _isLoading = false;
    }

    public void AddItem(WindowInfo info)
    {
        BlackList.Add(new EditableWindowInfo(info));
        SaveConfig();
    }

    public void UpdateItem(EditableWindowInfo item, WindowInfo info)
    {
        item.ProcessName = info.ProcessName ?? string.Empty;
        item.WindowTitle = info.WindowTitle ?? string.Empty;
        item.ExecutableName = info.ExecutableName ?? string.Empty;
        SaveConfig();
    }

    public void RemoveItem(EditableWindowInfo item)
    {
        BlackList.Remove(item);
        SaveConfig();
    }

    [RelayCommand]
    private void StartCapture()
    {
        StopCapture();
        if (_captureService.TryStart(out Hotkey? hotkey) && hotkey is not null)
        {
            IsCapturing = true;
            CaptureHint = $"请切换到目标窗口，按 {string.Join(" + ", hotkey.Keys)}，等待 1 秒后再切回。";
            return;
        }

        CaptureHint = "无法注册临时全局快捷键，请手动添加程序。";
    }

    [RelayCommand]
    public void StopCapture()
    {
        _captureService.Stop();
        IsCapturing = false;
        CaptureHint = string.Empty;
    }

    private void OnWindowCaptured(WindowInfo info)
    {
        IsCapturing = false;
        CaptureHint = string.Empty;
        WindowCaptured?.Invoke(info);
    }

    private void SaveConfig()
    {
        if (_isLoading)
        {
            return;
        }

        _config = new HotkeyBlacklistConfig
        {
            Enabled = IsEnabled,
            BlackList = BlackList.Where(item => !item.IsEmpty).Select(item => item.ToWindowInfo()).ToList()
        };
        _configManager.SetConfig(_config);
    }

    private void UpdateSuspendedStatus()
    {
        IsSystemHotkeysSuspended = _hotkeyManager.IsSystemHotkeysSuspended;
    }
}
