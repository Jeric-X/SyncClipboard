using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Keyboard;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryManager historyManager;
    private readonly IClipboardFactory clipboardFactory;
    private readonly VirtualKeyboard keyboard;
    public HistoryViewModel(HistoryManager historyManager, IClipboardFactory clipboardFactory, VirtualKeyboard keyboard)
    {
        this.historyManager = historyManager;
        this.clipboardFactory = clipboardFactory;
        this.keyboard = keyboard;
        this.historyManager.HistoryChanged += Refresh;
    }

    public List<HistoryRecord> HistoryItems
    {
        get
        {
            var list = historyManager.GetHistory();
            return list;
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(HistoryItems));
    }

    [RelayCommand]
    public Task DeleteItem(HistoryRecord record)
    {
        return historyManager.DeleteHistory(record);
    }

    public async Task CopyToClipboard(HistoryRecord record, bool paste, CancellationToken token)
    {
        var profile = await clipboardFactory.CreateProfileFromHistoryRecord(record, token);
        await profile.SetLocalClipboard(false, token);
        if (paste)
        {
            keyboard.Paste();
        }
    }
}