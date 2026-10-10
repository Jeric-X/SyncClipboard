using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using System.Text;

namespace SyncClipboard.Core.Utilities.History;

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
            FileStream stream;
            for (var i = 0; ; i++)
            {
                var candidate = Path.Combine(directory, $"{basename}-not-exported{(i == 0 ? "" : $"-{i}")}.txt");
                try
                {
                    stream = new(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    report = candidate;
                    break;
                }
                catch (IOException) when (File.Exists(candidate)) { }
            }
            await using (stream)
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteLineAsync(Strings.HistoryExportReport);
                await writer.WriteLineAsync(DateTimeOffset.Now.ToString("O"));
                await writer.WriteLineAsync(result.ArchivePath ?? Strings.HistoryExportNoArchive);
                await writer.WriteLineAsync(string.Format(Strings.HistoryExportRequestedCount, result.ExportedCount + result.Skipped.Count));
                await writer.WriteLineAsync(FormatResult(result));
                if (result.Error is not null)
                    await writer.WriteLineAsync(SingleLine(result.Error));
                foreach (var failure in result.Skipped)
                {
                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync(SingleLine(failure.ProfileId));
                    await writer.WriteLineAsync($"{failure.Reason}: {Describe(failure.Reason)}");
                    if (failure.Item is { } item)
                    {
                        await writer.WriteLineAsync(item.Timestamp.ToUniversalTime().ToString("O"));
                        await writer.WriteLineAsync(SingleLine(item.From));
                        var preview = string.Concat(item.Content.Text.EnumerateRunes().Take(200).Select(rune => rune.ToString()));
                        await writer.WriteLineAsync(SingleLine(preview) + (preview.Length < item.Content.Text.Length ? "…" : ""));
                        foreach (var reference in item.Content.FilePaths.Append(item.Content.TransferDataFile).OfType<string>().Distinct())
                        {
                            string path;
                            try
                            {
                                path = Profile.GetFullPath(persistentDir, item.Content.Type, item.Content.Hash, reference);
                            }
                            catch (ArgumentException)
                            {
                                path = reference;
                            }
                            await writer.WriteLineAsync(SingleLine(path));
                        }
                    }
                    if (failure.Detail is not null)
                        await writer.WriteLineAsync(SingleLine(failure.Detail));
                }
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

    private static string SingleLine(string text) => text.Replace("\r", "\\r").Replace("\n", "\\n");
}
