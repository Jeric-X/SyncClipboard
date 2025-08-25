using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryManager historyManager;
    public HistoryViewModel(HistoryManager historyManager)
    {
        this.historyManager = historyManager;
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
}