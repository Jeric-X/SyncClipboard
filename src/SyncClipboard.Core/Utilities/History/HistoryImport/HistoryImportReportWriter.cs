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

    public static async Task WriteAsync(string backup, HistoryImportResult result, CancellationToken token)
    {
        string? path = null;
        try
        {
            path = Path.Combine(Path.GetDirectoryName(backup)!, $"{Path.GetFileNameWithoutExtension(backup)}-not-imported-{Guid.NewGuid():N}.txt");
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1, useAsync: true))
                await WriteContentsAsync(stream, backup, result, token).ConfigureAwait(false);
            result.ReportPath = path;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            result.Canceled = true;
        }
        catch (Exception ex)
        {
            result.ReportError = ex.Message;
        }
        finally
        {
            if (path is not null && result.ReportPath is null)
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

    internal static async Task WriteContentsAsync(Stream stream, string backup, HistoryImportResult result, CancellationToken token)
    {
        await WriteLineAsync(stream, Strings.HistoryImportReport, token).ConfigureAwait(false);
        await WriteLineAsync(stream, backup, token).ConfigureAwait(false);
        await WriteLineAsync(stream, FormatResult(result), token).ConfigureAwait(false);
        foreach (var failure in result.Failures)
            await WriteLineAsync(stream, $"{failure.Index}. {SingleLine(failure.ProfileId)}: {SingleLine(failure.Reason)}", token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static ValueTask WriteLineAsync(Stream stream, string line, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return stream.WriteAsync(Encoding.UTF8.GetBytes(line + Environment.NewLine), token);
    }

    private static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
