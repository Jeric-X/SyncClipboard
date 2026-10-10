using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel
{
    private int _modalOperations;
    public bool HasModalOperation => _modalOperations > 0;

    public IReadOnlyList<HistoryRecordKey> GetExportSelection() => selectedHistoryRecords.Keys.ToArray();

    public IDisposable HoldForModalOperation()
    {
        _modalOperations++;
        return new ScopeGuard(() => _modalOperations--);
    }
}
