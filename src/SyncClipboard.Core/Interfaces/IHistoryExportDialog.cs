using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IHistoryExportDialog
{
    Task ShowAsync(IReadOnlyList<HistoryRecordKey>? selected = null, bool fromHistoryWindow = false);
}
