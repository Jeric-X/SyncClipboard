using SyncClipboard.Core.I18n;
using System.Text;

namespace SyncClipboard.Core.Utilities.History.HistoryImport;

internal sealed class HistoryImportReportWriter(string backup, HistoryImportResult result) : IAsyncDisposable
{
    private FileStream? _stream;
    private string? _path;

    public static string FormatResult(HistoryImportResult result)
    {
        var text = string.Format(Strings.HistoryImportResult, result.ImportedCount);
        if (result.RepairedCount > 0)
            text += string.Format(Strings.HistoryImportRepairedCount, result.RepairedCount);
        if (result.ExistingCount > 0)
            text += string.Format(Strings.HistoryImportExistingCount, result.ExistingCount);
        if (result.FailureCount > 0)
            text += string.Format(Strings.HistoryImportFailureCount, result.FailureCount);
        if (result.Error is not null)
            text += "\n" + string.Format(Strings.HistoryImportFailed, result.Error);
        return text;
    }

    public async Task<bool> AppendAsync(HistoryImportFailure failure, CancellationToken token)
    {
        var line = $"{failure.Index}. {SingleLine(failure.ProfileId)}: {SingleLine(failure.Reason)}";
        try
        {
            token.ThrowIfCancellationRequested();
            if (_stream is null)
            {
                var path = Path.Combine(Path.GetDirectoryName(backup)!, $"{Path.GetFileNameWithoutExtension(backup)}-not-imported-{Guid.NewGuid():N}.txt");
                _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1, useAsync: true);
                _path = path;
                await WriteLineAsync(_stream, Strings.HistoryImportReport, token).ConfigureAwait(false);
                await WriteLineAsync(_stream, backup, token).ConfigureAwait(false);
            }
            await WriteLineAsync(_stream, line, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            result.Canceled = true;
        }
        catch (Exception ex)
        {
            result.ReportError = ex.Message + "\n" + line;
        }
        return false;
    }

    public async Task CompleteAsync(CancellationToken token)
    {
        if (_stream is null || result.ReportError is not null)
            return;
        try
        {
            await WriteLineAsync(_stream, FormatResult(result), token).ConfigureAwait(false);
            await _stream.FlushAsync(token).ConfigureAwait(false);
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
            result.ReportPath = _path;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            result.Canceled = true;
        }
        catch (Exception ex)
        {
            result.ReportError = ex.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_stream is not null)
                await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!result.Canceled)
                result.ReportError ??= ex.Message;
        }
        finally
        {
            _stream = null;
            if (_path is not null && result.ReportPath is null)
            {
                try
                {
                    File.Delete(_path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal static ValueTask WriteLineAsync(Stream stream, string line, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return stream.WriteAsync(Encoding.UTF8.GetBytes(line + Environment.NewLine), token);
    }

    private static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
