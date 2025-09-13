using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using ObservableCollections;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private IWindow window = null!;

    private readonly ConfigManager configManager;
    private readonly HistoryManager historyManager;
    private readonly IClipboardFactory clipboardFactory;
    private readonly VirtualKeyboard keyboard;
    private readonly INotificationManager notificationManager;
    private readonly ConfigBase runtimeConfig;
    private readonly ILogger logger;

    public HistoryViewModel(
        ConfigManager configManager,
        HistoryManager historyManager,
        IClipboardFactory clipboardFactory,
        VirtualKeyboard keyboard,
        INotificationManager notificationManager,
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig,
        ILogger logger)
    {
        this.configManager = configManager;
        this.historyManager = historyManager;
        this.clipboardFactory = clipboardFactory;
        this.keyboard = keyboard;
        this.notificationManager = notificationManager;
        this.runtimeConfig = runtimeConfig;
        this.logger = logger;

        viewController = allHistoryItems.CreateView(x => x);
        HistoryItems = viewController.ToNotifyCollectionChanged();

        // 设置初始过滤器
        ApplyFilter();
    }

    partial void OnSelectedFilterChanged(HistoryFilterType value)
    {
        ApplyFilter();
        OnPropertyChanged(nameof(SelectedFilterOption));
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        viewController.AttachFilter(record =>
        {
            // 首先应用类型过滤
            var typeMatches = SelectedFilter switch
            {
                HistoryFilterType.All => true,
                HistoryFilterType.Text => record.Type == ProfileType.Text,
                HistoryFilterType.Image => record.Type == ProfileType.Image,
                HistoryFilterType.File => record.Type == ProfileType.File || record.Type == ProfileType.Group,
                HistoryFilterType.Starred => record.Stared,
                _ => true
            };

            // 如果类型不匹配，直接返回false
            if (!typeMatches) return false;

            // 如果没有搜索文本，返回类型匹配结果
            if (string.IsNullOrEmpty(SearchText)) return true;

            // 应用搜索文本过滤
            var searchText = SearchText;

            // 搜索文本内容
            if (!string.IsNullOrEmpty(record.Text) &&
                record.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 搜索文件名
            if (record.FilePath?.Length > 0)
            {
                foreach (var filePath in record.FilePath)
                {
                    var fileName = Path.GetFileName(filePath);
                    if (!string.IsNullOrEmpty(fileName) &&
                        fileName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        });
    }

    [ObservableProperty]
    private int selectedIndex = -1;

    [ObservableProperty]
    private HistoryFilterType selectedFilter = HistoryFilterType.All;

    [ObservableProperty]
    private string searchText = string.Empty;

    public LocaleString<HistoryFilterType> SelectedFilterOption
    {
        get => FilterOptions.FirstOrDefault(x => x.Key.Equals(SelectedFilter)) ?? FilterOptions[0];
        set
        {
            if (value != null)
            {
                SelectedFilter = value.Key;
            }
        }
    }

    public List<LocaleString<HistoryFilterType>> FilterOptions { get; } =
        [
            new(HistoryFilterType.All, I18n.Strings.HistoryFilterAll),
            new(HistoryFilterType.Text, I18n.Strings.HistoryFilterText),
            new(HistoryFilterType.Image, I18n.Strings.HistoryFilterImage),
            new(HistoryFilterType.File, I18n.Strings.HistoryFilterFile),
            new(HistoryFilterType.Starred, I18n.Strings.HistoryFilterStarred)
        ];

    public INotifyCollectionChangedSynchronizedViewList<HistoryRecordVM> HistoryItems { get; }
    private readonly ISynchronizedView<HistoryRecordVM, HistoryRecordVM> viewController;
    private readonly ObservableList<HistoryRecordVM> allHistoryItems = [];

    public int Width
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().Width;
        set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { Width = value });
    }

    public int Height
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().Height;
        set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { Height = value });
    }

    public bool IsTopmost
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().IsTopmost;
        set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { IsTopmost = value });
    }

    public void ToggleTopmost()
    {
        IsTopmost = !IsTopmost;
        window?.SetTopmost(IsTopmost);
        OnPropertyChanged(nameof(IsTopmost));
    }

    [RelayCommand]
    public Task DeleteItem(HistoryRecordVM record)
    {
        return historyManager.DeleteHistory(record.ToHistoryRecord());
    }

    [RelayCommand]
    public Task ChangeStarStatus(HistoryRecordVM record)
    {
        record.Stared = !record.Stared;
        return historyManager.UpdateHistory(record.ToHistoryRecord());
    }

    [RelayCommand]
    public Task ChangePinStatus(HistoryRecordVM record)
    {
        record.Pinned = !record.Pinned;
        return historyManager.UpdateHistory(record.ToHistoryRecord());
    }

    [RelayCommand]
    public void CtrlHome()
    {
        ScrollToTop();
    }

    [RelayCommand]
    public void CtrlEnd()
    {
        ScrollToBottom();
    }

    public void NavigateToNextFilter()
    {
        var currentIndex = (int)SelectedFilter;
        var filterCount = FilterOptions.Count;
        var nextIndex = (currentIndex + 1) % filterCount;
        SelectedFilter = (HistoryFilterType)nextIndex;
        window?.ScrollToTop();
    }

    public void NavigateToPreviousFilter()
    {
        var currentIndex = (int)SelectedFilter;
        var filterCount = FilterOptions.Count;
        var prevIndex = (currentIndex - 1 + filterCount) % filterCount;
        SelectedFilter = (HistoryFilterType)prevIndex;
        window?.ScrollToTop();
    }

    public void NavigateDown()
    {
        var count = ((ICollection<HistoryRecordVM>)HistoryItems).Count;
        if (count == 0) return;

        var maxIndex = count - 1;
        if (SelectedIndex < maxIndex)
        {
            SelectedIndex++;
        }
        window?.ScrollToSelectedItem();
    }

    public void NavigateUp()
    {
        var count = ((ICollection<HistoryRecordVM>)HistoryItems).Count;
        if (count == 0) return;

        if (SelectedIndex > 0)
        {
            SelectedIndex--;
        }
        window?.ScrollToSelectedItem();
    }

    public void NavigateToFirst()
    {
        var count = ((ICollection<HistoryRecordVM>)HistoryItems).Count;
        if (count == 0) return;

        SelectedIndex = 0;
        window?.ScrollToSelectedItem();
    }

    public void NavigateToLast()
    {
        var count = ((ICollection<HistoryRecordVM>)HistoryItems).Count;
        if (count == 0) return;

        SelectedIndex = count - 1;
        window?.ScrollToSelectedItem();
    }

    public bool HandleKeyPress(Key key, bool isShiftPressed = false, bool isAltPressed = false, bool isCtrlPressed = false)
    {
        if (isCtrlPressed)
        {
            switch (key)
            {
                case Key.Home:
                    ScrollToTop();
                    return true;
                case Key.End:
                    ScrollToBottom();
                    return true;
            }
        }

        switch (key)
        {
            case Key.Tab:
                if (isShiftPressed)
                    NavigateToPreviousFilter();
                else
                    NavigateToNextFilter();
                return true;

            case Key.Down:
                NavigateDown();
                return true;

            case Key.Up:
                NavigateUp();
                return true;

            case Key.Enter:
                HandleEnterKey(isAltPressed);
                return true;

            case Key.Esc:
                window?.Close();
                return true;

            default:
                return false;
        }
    }

    private async void HandleEnterKey(bool isAltPressed)
    {
        var count = ((ICollection<HistoryRecordVM>)HistoryItems).Count;
        if (SelectedIndex < 0 || SelectedIndex >= count)
            return;

        var selectedItem = ((IList<HistoryRecordVM>)HistoryItems)[SelectedIndex];
        if (selectedItem == null) return;

        // Alt键表示不粘贴到剪贴板，只是复制操作
        var paste = !isAltPressed;
        await CopyToClipboard(selectedItem, paste, CancellationToken.None);
    }

    public void ViewImage(HistoryRecordVM record)
    {
        if (record.FilePath.Length == 0 || !File.Exists(record.FilePath[0]))
        {
            return;
        }
        _remainWindowForViewDetail = true;
        Sys.OpenWithDefaultApp(record.FilePath[0]);
    }

    private void ScrollToTop()
    {
        if (HistoryItems.Any())
        {
            SelectedIndex = 0;
            window?.ScrollToSelectedItem();
        }
    }

    private void ScrollToBottom()
    {
        if (HistoryItems.Any())
        {
            SelectedIndex = ((IList<HistoryRecordVM>)HistoryItems).Count - 1;
            window?.ScrollToSelectedItem();
        }
    }

    public async Task Init(IWindow window)
    {
        this.window = window;
        var records = await HistoryManager.GetHistory();
        allHistoryItems.AddRange(records.Select(x => new HistoryRecordVM(x)));

        historyManager.HistoryAdded += record => allHistoryItems.Insert(0, new HistoryRecordVM(record));
        historyManager.HistoryRemoved += record => allHistoryItems.Remove(new HistoryRecordVM(record));
        historyManager.HistoryUpdated += record =>
        {
            var newRecord = new HistoryRecordVM(record);
            var oldRecord = allHistoryItems.FirstOrDefault(r => r == newRecord);
            if (oldRecord == null)
            {
                return;
            }
            oldRecord.Update(newRecord);
        };
    }

    public async Task CopyToClipboard(HistoryRecordVM record, bool paste, CancellationToken token)
    {
        foreach (var path in record.FilePath)
        {
            if (!File.Exists(path))
            {
                logger.Write("WARNING", $"{I18n.Strings.UnableToCopyByMissingFile}。Path: {path}, Hash: {record.Hash}, Text: {record.Text}");
                notificationManager.SharedQuickMessage(record.Text, I18n.Strings.UnableToCopyByMissingFile);
                return;
            }
        }

        SelectedIndex = -1;
        window.ScrollToTop();
        window.Close();
        SearchText = string.Empty;

        var profile = await clipboardFactory.CreateProfileFromHistoryRecord(record.ToHistoryRecord(), token);
        await profile.SetLocalClipboard(false, token);
        if (paste)
        {
            keyboard.Paste();
        }
    }

    public void OnGotFocus()
    {
        _remainWindowForViewDetail = false;
    }

    private bool _remainWindowForViewDetail = false;

    public void OnLostFocus()
    {
        if (!_remainWindowForViewDetail && !IsTopmost && configManager.GetConfig<HistoryConfig>().CloseWhenLostFocus)
        {
            window.Close();
        }
    }

    public void HandleItemDoubleClick(HistoryRecordVM record)
    {
        _ = CopyToClipboard(record, false, CancellationToken.None);
    }

    public void HandleImageDoubleClick(HistoryRecordVM record)
    {
        ViewImage(record);
    }
}