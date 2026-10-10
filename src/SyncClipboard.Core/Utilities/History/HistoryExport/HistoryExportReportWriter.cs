using SyncClipboard.Core.I18n;
using System.Text;

namespace SyncClipboard.Core.Utilities.History.HistoryExport;

internal static class HistoryExportReportWriter
{
    public static string Describe(HistoryExportFailure reason) => reason switch
    {
        HistoryExportFailure.MissingFile => Strings.HistoryExportMissing,
        HistoryExportFailure.InvalidData => Strings.HistoryExportInvalid,
        HistoryExportFailure.ReadFailed => Strings.HistoryExportReadFailed,
        HistoryExportFailure.RecordRemoved => Strings.HistoryExportRemoved,
        HistoryExportFailure.Unsupported => Strings.HistoryExportUnsupported,
        HistoryExportFailure.Canceled => Strings.HistoryExportCanceled,
        _ => Strings.HistoryExportNotSaved
    };

    public static string FormatResult(HistoryExportResult result)
    {
        var summary = string.Format(Strings.HistoryExportResult, result.ExportedCount);
        if (result.MissingCount > 0)
            summary += string.Format(Strings.HistoryExportMissingCount, result.MissingCount);
        if (result.OtherCount > 0)
            summary += string.Format(Strings.HistoryExportOtherCount, result.OtherCount);
        return summary;
    }

    public static async Task WriteAsync(HistoryExportResult result, string directory, string basename, string persistentDir)
    {
        string? report = null;
        try
        {
            await using (var stream = CreateReport(directory, basename))
            {
                report = stream.Name;
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                await WriteSummaryAsync(writer, result);
                foreach (var failure in result.Skipped)
                    await WriteFailureAsync(writer, failure, persistentDir);
            }
            result.ReportPath = report;
        }
        catch (Exception ex)
        {
            result.ReportError = ex.Message;
            if (report is not null)
            {
                try
                {
                    File.Delete(report);
                }
                catch (Exception cleanupError)
                {
                    result.ReportError += $"\n{report}: {cleanupError.Message}";
                }
            }
        }
    }

    private static FileStream CreateReport(string directory, string basename)
    {
        for (var i = 0; ; i++)
        {
            var candidate = Path.Combine(directory, $"{basename}-not-exported{(i == 0 ? "" : $"-{i}")}.txt");
            try
            {
                return new(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (File.Exists(candidate)) { }
        }
    }

    private static async Task WriteSummaryAsync(StreamWriter writer, HistoryExportResult result)
    {
        await writer.WriteLineAsync(Strings.HistoryExportReport);
        await writer.WriteLineAsync(DateTimeOffset.Now.ToString("O"));
        await writer.WriteLineAsync(result.ArchivePath ?? Strings.HistoryExportNoArchive);
        await writer.WriteLineAsync(string.Format(Strings.HistoryExportRequestedCount, result.ExportedCount + result.Skipped.Count));
        await writer.WriteLineAsync(FormatResult(result));
        if (result.Error is not null)
            await writer.WriteLineAsync(SingleLine(result.Error));
    }

    private static async Task WriteFailureAsync(StreamWriter writer, HistoryExportSkipped failure, string persistentDir)
    {
        await writer.WriteLineAsync();
        await writer.WriteLineAsync(SingleLine(failure.ProfileId));
        await writer.WriteLineAsync($"{failure.Reason}: {Describe(failure.Reason)}");
        if (failure.Item is { } item)
            await WriteRecordAsync(writer, item, persistentDir);
        if (failure.Detail is not null)
            await writer.WriteLineAsync(SingleLine(failure.Detail));
    }

    private static async Task WriteRecordAsync(StreamWriter writer, HistoryExportRecord item, string persistentDir)
    {
        await writer.WriteLineAsync(item.Timestamp.ToUniversalTime().ToString("O"));
        await writer.WriteLineAsync(SingleLine(item.From));
        var preview = string.Concat(item.Text.EnumerateRunes().Take(200).Select(rune => rune.ToString()));
        await writer.WriteLineAsync(SingleLine(preview) + (preview.Length < item.Text.Length ? "…" : ""));
        foreach (var reference in item.FilePaths.Append(item.TransferDataFile).OfType<string>().Distinct())
        {
            string path;
            try
            {
                path = Profile.GetFullPath(persistentDir, item.ProfileType, item.Hash, reference);
            }
            catch (ArgumentException)
            {
                path = reference;
            }
            await writer.WriteLineAsync(SingleLine(path));
        }
    }

    private static string SingleLine(string text) => text.Replace("\r", "\\r").Replace("\n", "\\n");
}
