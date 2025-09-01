using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.ViewModels.Sub;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel(
    HistoryManager historyManager,
    IClipboardFactory clipboardFactory,
    VirtualKeyboard keyboard,
    INotificationManager notificationManager,
    [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig) : ObservableObject
{
    [ObservableProperty]
    public ObservableCollection<HistoryRecordVM> historyItems = [];

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

    public async Task Init()
    {
        var records = await HistoryManager.GetHistory();
        foreach (var record in records)
        {
            HistoryItems.Add(new HistoryRecordVM(record));
        }

        historyManager.HistoryAdded += record => HistoryItems.Insert(0, new HistoryRecordVM(record));
        historyManager.HistoryRemoved += record => HistoryItems.Remove(new HistoryRecordVM(record));
        historyManager.HistoryUpdated += record =>
        {
            var newRecord = new HistoryRecordVM(record);
            var oldRecord = HistoryItems.FirstOrDefault(r => r == newRecord);
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
                notificationManager.SharedQuickMessage(record.Text, I18n.Strings.UnableToCopyByMissingFile);
                return;
            }
        }

        var profile = await clipboardFactory.CreateProfileFromHistoryRecord(record.ToHistoryRecord(), token);
        await profile.SetLocalClipboard(false, token);
        if (paste)
        {
            keyboard.Paste();
        }
    }
}