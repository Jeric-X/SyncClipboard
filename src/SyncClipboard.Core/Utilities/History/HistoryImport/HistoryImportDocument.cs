using SyncClipboard.Core.Utilities.History.HistoryExport;
using System.Collections;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.History.HistoryImport;

public sealed record HistoryImportDocument(
    string Format, int FormatVersion, string AppVersion, DateTime ExportedAt, JsonElement Records);

internal sealed class HistoryImportRecords(JsonElement records) : IReadOnlyList<HistoryExportRecord?>
{
    public int Count => records.GetArrayLength();
    public HistoryExportRecord? this[int index] => records[index].Deserialize<HistoryExportRecord>(HistoryExporter.JsonOptions);

    public IEnumerator<HistoryExportRecord?> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
