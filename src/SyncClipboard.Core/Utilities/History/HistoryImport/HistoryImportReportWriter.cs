using SyncClipboard.Core.I18n;
using System.Text;

namespace SyncClipboard.Core.Utilities.History.HistoryImport;

internal static class HistoryImportReportWriter
{
    public static string FormatResult(HistoryImportResult result)
    {
        var text = string.Format(Strings.HistoryImportResult, result.ImportedCount);
        if (result.RepairedCount > 0)
            text += string.Format(Strings.HistoryImportRepairedCount, result.RepairedCount);
        if (result.ExistingCount > 0)
            text += string.Format(Strings.HistoryImportExistingCount, result.ExistingCount);
        if (result.Failures.Count > 0)
            text += string.Format(Strings.HistoryImportFailureCount, result.Failures.Count);
        if (result.Error is not null)
            text += "\n" + string.Format(Strings.HistoryImportFailed, result.Error);
        return text;
    }

    public static async Task WriteAsync(string backup, HistoryImportResult result)
    {
        string? path = null;
        try
        {
            path = Path.Combine(Path.GetDirectoryName(backup)!, $"{Path.GetFileNameWithoutExtension(backup)}-not-imported-{Guid.NewGuid():N}.txt");
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                await writer.WriteLineAsync(Strings.HistoryImportReport);
                await writer.WriteLineAsync(backup);
                await writer.WriteLineAsync(FormatResult(result));
                foreach (var failure in result.Failures)
                    await writer.WriteLineAsync($"{failure.Index}. {SingleLine(failure.ProfileId)}: {SingleLine(failure.Reason)}");
                await writer.FlushAsync();
            }
            result.ReportPath = path;
        }
        catch (Exception ex)
        {
            result.ReportError = ex.Message;
            if (path is not null)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
