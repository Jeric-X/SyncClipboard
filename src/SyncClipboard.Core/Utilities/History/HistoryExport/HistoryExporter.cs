using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.History.HistoryExport;

public sealed class HistoryExporter(IProfileEnv profileEnv)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private int _sessionActive;

    public IDisposable? TryBeginSession() => Interlocked.CompareExchange(ref _sessionActive, 1, 0) == 0
        ? new ScopeGuard(() => Interlocked.Exchange(ref _sessionActive, 0)) : null;

    public async Task<HistoryExportPlan> EstimateAsync(
        IReadOnlyList<HistoryExportRecord> items, IReadOnlyList<HistoryRecordKey>? selected, CancellationToken token)
    {
        var ordered = items.OrderByDescending(item => item.Timestamp)
            .ThenBy(item => item.ProfileId, StringComparer.Ordinal).ToArray();
        var existing = ordered.Select(item => item.ProfileId).ToHashSet(StringComparer.Ordinal);
        var removed = selected?.Select(key => Profile.GetProfileId(key.Type, key.Hash.ToUpperInvariant()))
            .Distinct().Where(id => !existing.Contains(id))
            .Select(id => new HistoryExportSkipped(id, null, HistoryExportFailure.RecordRemoved)).ToArray() ?? [];
        List<HistoryExportRecord> prepared = [];
        List<HistoryExportSkipped> skipped = [.. removed];
        long size = 1024;
        foreach (var item in ordered)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var record = await PrepareTransferAsync(item, token).ConfigureAwait(false);
                var transferSize = record.TransferData?.Size ?? 0;
                var recordSize = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(record, JsonOptions)) + 512;
                // Retain prepared data in this plan without changing the database snapshot.
                prepared.Add(record);
                size += recordSize + transferSize;
            }
            catch (Exception ex) when (!token.IsCancellationRequested && IsSourceError(ex))
            {
                skipped.Add(new(item.ProfileId, item, Classify(ex), ex.Message));
            }
        }
        return new(prepared, skipped, prepared.Count > 0 ? size : 0);
    }

    public async Task<HistoryExportResult> ExportAsync(
        HistoryExportPlan plan, string directory, IProgress<HistoryExportProgress>? progress, CancellationToken token)
    {
        var result = new HistoryExportResult();
        result.Skipped.AddRange(plan.Skipped);
        var basename = $"SyncClipboard-history-{DateTime.Now:yyyyMMdd-HHmmss}";
        await WriteArchiveAsync(plan.Items, directory, basename, result, progress, token).ConfigureAwait(false);
        MarkUnexportedRecords(plan, result);
        if (result.Skipped.Count > 0)
        {
            var reportBase = result.ArchivePath is { } path ? Path.GetFileNameWithoutExtension(path) : basename;
            await HistoryExportReportWriter.WriteAsync(result, directory, reportBase, profileEnv.GetHistoryPersistentDir()).ConfigureAwait(false);
        }
        return result;
    }

    private static async Task WriteArchiveAsync(
        IReadOnlyList<HistoryExportRecord> items, string directory, string basename, HistoryExportResult result,
        IProgress<HistoryExportProgress>? progress, CancellationToken token)
    {
        var partial = Path.Combine(directory, $".{basename}-{Guid.NewGuid():N}.partial");
        var ownsPartial = false;
        FileStream? output = null;
        ZipArchive? archive = null;
        FileStream OpenOutput()
        {
            var stream = CreateOutput(partial);
            ownsPartial = true;
            return stream;
        }
        ZipArchive OpenArchive()
        {
            output ??= OpenOutput();
            return archive ??= new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        }
        try
        {
            var records = await WriteRecordsAsync(items, OpenArchive, result, progress, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (records.Count == 0)
                return;
            output ??= OpenOutput();
            await WriteDocumentAsync(records, archive, output, token).ConfigureAwait(false);
            var extension = archive is null ? ".json" : ".zip";
            archive?.Dispose();
            archive = null;
            await output.FlushAsync(token).ConfigureAwait(false);
            await output.DisposeAsync().ConfigureAwait(false);
            output = null;
            token.ThrowIfCancellationRequested();
            result.ArchivePath = Commit(partial, directory, basename, extension);
            result.ExportedCount = records.Count;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            result.Canceled = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        finally
        {
            await CleanupOutputAsync(archive, output, ownsPartial ? partial : null, result).ConfigureAwait(false);
        }
    }

    private static async Task<List<HistoryExportRecord>> WriteRecordsAsync(
        IReadOnlyList<HistoryExportRecord> items, Func<ZipArchive> openArchive, HistoryExportResult result,
        IProgress<HistoryExportProgress>? progress, CancellationToken token)
    {
        var records = new List<HistoryExportRecord>();
        var processed = new HashSet<string>(StringComparer.Ordinal);
        long written = 0;
        var lastProgress = System.Diagnostics.Stopwatch.StartNew();
        void ReportProgress() => progress?.Report(new(processed.Count, items.Count, written));
        void ReportBytes(int count)
        {
            written += count;
            if (lastProgress.ElapsedMilliseconds >= 100)
            {
                ReportProgress();
                lastProgress.Restart();
            }
        }
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            if (processed.Contains(item.ProfileId))
                throw new InvalidDataException($"Duplicate Profile ID: {item.ProfileId}");
            FileStream? source;
            try
            {
                source = await OpenTransferAsync(item, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!token.IsCancellationRequested && IsSourceError(ex))
            {
                result.Skipped.Add(new(item.ProfileId, item, Classify(ex), ex.Message));
                processed.Add(item.ProfileId);
                ReportProgress();
                continue;
            }
            await using (source)
            {
                if (source is not null)
                    await WriteTransferAsync(item, source, openArchive(), result, ReportBytes, token).ConfigureAwait(false);
                records.Add(item);
                processed.Add(item.ProfileId);
                ReportProgress();
            }
        }
        return records;
    }

    private static async Task<FileStream?> OpenTransferAsync(HistoryExportRecord item, CancellationToken token)
    {
        ValidateSegment(item.ProfileId);
        if (item.TransferData is not { } transfer)
            return null;
        var source = new FileStream(item.TransferDataFile ?? throw new FileNotFoundException("Transfer data is missing."),
            FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (source.Length != transfer.Size)
                throw new InvalidDataException("Transfer data length changed before export.");
            // Validate the opened handle before creating a ZIP entry. Recheck bytes during the copy.
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(source, token).ConfigureAwait(false));
            if (!Utility.SHA256Same(actual, transfer.Sha256))
                throw new InvalidDataException("Transfer data changed before export.");
            source.Position = 0;
            return source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static async Task WriteTransferAsync(
        HistoryExportRecord item, Stream source, ZipArchive archive, HistoryExportResult result,
        Action<int> progress, CancellationToken token)
    {
        var transfer = item.TransferData!;
        var entry = archive.CreateEntry(transfer.Path, CompressionLevel.NoCompression);
        await using var destination = entry.Open();
        void SourceFailed(Exception ex) => result.Skipped.Add(new(item.ProfileId, item, Classify(ex), ex.Message));
        var copied = await CopyVerifiedAsync(source, destination, transfer.Sha256, progress, token, SourceFailed).ConfigureAwait(false);
        if (copied != transfer.Size)
        {
            var error = new InvalidDataException("Transfer data length changed during export.");
            SourceFailed(error);
            throw error;
        }
    }

    private static async Task WriteDocumentAsync(
        List<HistoryExportRecord> records, ZipArchive? archive, Stream output, CancellationToken token)
    {
        var document = new HistoryExportDocument("syncclipboard-history", 1, Env.AppVersion, DateTime.UtcNow, records);
        if (archive is not null)
        {
            await using var json = archive.CreateEntry("history.json").Open();
            await JsonSerializer.SerializeAsync(json, document, JsonOptions, token).ConfigureAwait(false);
        }
        else
        {
            await JsonSerializer.SerializeAsync(output, document, JsonOptions, token).ConfigureAwait(false);
        }
    }

    private static async Task CleanupOutputAsync(
        ZipArchive? archive, FileStream? output, string? partial, HistoryExportResult result)
    {
        try
        {
            archive?.Dispose();
        }
        catch (Exception ex)
        {
            result.Error ??= ex.Message;
        }
        try
        {
            if (output is not null)
                await output.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result.Error ??= ex.Message;
        }
        try
        {
            if (partial is not null)
                File.Delete(partial);
        }
        catch (Exception ex)
        {
            result.Error = $"{result.Error}\n{partial}: {ex.Message}";
        }
    }

    private static void MarkUnexportedRecords(HistoryExportPlan plan, HistoryExportResult result)
    {
        if (result.ArchivePath is not null || (!result.Canceled && result.Error is null))
            return;
        var skipped = result.Skipped.Select(item => item.ProfileId).ToHashSet();
        foreach (var item in plan.Items.Where(item => !skipped.Contains(item.ProfileId)))
            result.Skipped.Add(new(item.ProfileId, item,
                result.Canceled ? HistoryExportFailure.Canceled : HistoryExportFailure.ArchiveFailed, result.Error));
    }

    private Profile CreateProfile(HistoryExportRecord item) => Profile.Create(profileEnv.GetHistoryPersistentDir(), new ProfilePersistentInfo
    {
        Type = item.ProfileType,
        Hash = item.Hash,
        Text = item.Text,
        Size = item.Size,
        FilePaths = item.FilePaths,
        TransferDataFile = item.TransferDataFile,
        TransferDataHash = item.TransferDataHash
    });

    private async Task<HistoryExportRecord> PrepareTransferAsync(HistoryExportRecord item, CancellationToken token)
    {
        Profile profile;
        try
        {
            profile = CreateProfile(item);
        }
        catch (NotSupportedException) when (Utility.IsValidSHA256(item.TransferDataHash) && item.TransferDataFile is not null)
        {
            var fallback = new FileHashInfo(Profile.GetFullPath(profileEnv.GetHistoryPersistentDir(), item.ProfileType,
                item.Hash, item.TransferDataFile), item.TransferDataHash);
            var actual = await Utility.CalculateFileSHA256(fallback.Path, token).ConfigureAwait(false);
            if (!Utility.SHA256Same(actual, fallback.Hash))
                throw new InvalidDataException("Transfer data hash mismatch.");
            return WithTransferData(item, fallback);
        }
        if (!await profile.IsDataComplete(true, token).ConfigureAwait(false))
            throw new FileNotFoundException("Local content is missing.");
        var transfer = await profile.PrepareTransferData(profileEnv.GetHistoryPersistentDir(), token).ConfigureAwait(false);
        if (profile.HasTransferData && transfer is null)
            throw new FileNotFoundException("Transfer data is missing.");
        return WithTransferData(item with { Size = await profile.GetSize(token).ConfigureAwait(false) }, transfer);
    }

    private static HistoryExportRecord WithTransferData(HistoryExportRecord item, FileHashInfo? transfer)
    {
        ValidateSegment(item.ProfileId);
        HistoryExportTransferData? data = null;
        if (transfer is not null)
        {
            var name = Path.GetFileName(transfer.Path);
            data = new(name, $"files/{item.ProfileId}/{FileSys.SafeFileName(name)}",
                new FileInfo(transfer.Path).Length, transfer.Hash.ToUpperInvariant());
        }
        return item with
        {
            TransferData = data,
            TransferDataFile = transfer?.Path,
            TransferDataHash = data?.Sha256
        };
    }

    internal static bool IsSourceError(Exception ex) => !FileSys.IsDiskFull(ex) &&
        ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    private static HistoryExportFailure Classify(Exception ex)
    {
        if (ex is FileNotFoundException or DirectoryNotFoundException)
            return HistoryExportFailure.MissingFile;
        if (ex.InnerException is not null)
            return Classify(ex.InnerException);
        return ex switch
        {
            NotSupportedException => HistoryExportFailure.Unsupported,
            InvalidDataException or LocalProfileDataUnavailableException or ArgumentException => HistoryExportFailure.InvalidData,
            _ => HistoryExportFailure.ReadFailed
        };
    }

    private static FileStream CreateOutput(string path) => new(path, FileMode.CreateNew, FileAccess.Write,
        FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    internal static string Commit(string partial, string directory, string basename, string extension)
    {
        for (var i = 0; ; i++)
        {
            var path = Path.Combine(directory, $"{basename}{(i == 0 ? "" : $"-{i}")}{extension}");
            try
            {
                File.Move(partial, path, overwrite: false);
                return path;
            }
            catch (IOException) when (File.Exists(path)) { }
        }
    }

    private static void ValidateSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("..") || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("Invalid Profile ID.");
    }

    internal static async Task<long> CopyVerifiedAsync(
        Stream source, Stream target, string expectedHash, Action<int> progress, CancellationToken token,
        Action<Exception>? sourceFailed = null)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        try
        {
            while (true)
            {
                int read;
                try
                {
                    read = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!token.IsCancellationRequested && IsSourceError(ex))
                {
                    sourceFailed?.Invoke(ex);
                    throw;
                }
                if (read == 0)
                    break;
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                total += read;
                progress(read);
            }
            if (!Utility.SHA256Same(Convert.ToHexString(hash.GetHashAndReset()), expectedHash))
            {
                var error = new InvalidDataException("Transfer data changed during export.");
                sourceFailed?.Invoke(error);
                throw error;
            }
            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
