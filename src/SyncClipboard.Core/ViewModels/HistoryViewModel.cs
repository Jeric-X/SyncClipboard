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
    public ObservableCollection<HistoryRecord> historyItems = [];

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
    public Task DeleteItem(HistoryRecord record)
    {
        return historyManager.DeleteHistory(record);
    }

    public async Task Init()
    {
        HistoryItems = new(await HistoryManager.GetHistory());
        historyManager.HistoryAdded += record => HistoryItems.Insert(0, record);
        historyManager.HistoryRemoved += record => HistoryItems.Remove(record);
        historyManager.HistoryUpdated += record =>
        {
            if (HistoryItems.FirstOrDefault() == record)
            {
                return;
            }
            HistoryItems.Remove(record);
            HistoryItems.Insert(0, record);
        };
    }

    public async Task CopyToClipboard(HistoryRecord record, bool paste, CancellationToken token)
    {
        foreach (var path in record.FilePath)
        {
            if (!File.Exists(path))
            {
                notificationManager.SharedQuickMessage(record.Text, I18n.Strings.UnableToCopyByMissingFile);
                return;
            }
        }

        var profile = await clipboardFactory.CreateProfileFromHistoryRecord(record, token);
        await profile.SetLocalClipboard(false, token);
        if (paste)
        {
            keyboard.Paste();
        }
    }
}