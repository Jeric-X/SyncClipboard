using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.History;

public sealed class HistoryExporter(IProfileEnv profileEnv)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private int _sessionActive;

    public IDisposable? TryBeginSession() => Interlocked.CompareExchange(ref _sessionActive, 1, 0) == 0
        ? new ScopeGuard(() => Interlocked.Exchange(ref _sessionActive, 0)) : null;

    public async Task<HistoryExportPlan> EstimateAsync(
        IReadOnlyList<HistoryExportItem> items, IReadOnlyList<HistoryRecordKey>? selected, CancellationToken token)
    {
        var ordered = items.OrderByDescending(item => item.Timestamp)
            .ThenBy(item => item.ProfileId, StringComparer.Ordinal).ToArray();
        var existing = ordered.Select(item => item.ProfileId).ToHashSet(StringComparer.Ordinal);
        var removed = selected?.Select(key => Profile.GetProfileId(key.Type, key.Hash.ToUpperInvariant()))
            .Distinct().Where(id => !existing.Contains(id))
            .Select(id => new HistoryExportSkipped(id, null, HistoryExportFailure.RecordRemoved)).ToArray() ?? [];
        List<HistoryExportItem> prepared = [];
        List<HistoryExportSkipped> skipped = [.. removed];
        var plan = new HistoryExportPlan(prepared, skipped, 0);
        long size = 1024;
        try
        {
            foreach (var item in ordered)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var transfer = await PrepareTransferAsync(item, plan, token).ConfigureAwait(false);
                    var transferSize = transfer is null ? 0 : new FileInfo(transfer.Path).Length;
                    var recordSize = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(ToRecord(item, null), JsonOptions)) + 512;
                    // Retain prepared data in this plan without changing the database snapshot.
                    prepared.Add(transfer is null ? item : item with
                    {
                        Content = item.Content with { TransferDataFile = transfer.Path, TransferDataHash = transfer.Hash }
                    });
                    size += recordSize + transferSize;
                }
                catch (Exception ex) when (!token.IsCancellationRequested && IsSourceError(ex))
                {
                    skipped.Add(new(item.ProfileId, item, Classify(ex), ex.Message));
                }
            }
            return plan with { EstimatedBytes = prepared.Count > 0 ? size : 0 };
        }
        catch
        {
            plan.Dispose();
            throw;
        }
    }

    public async Task<HistoryExportResult> ExportAsync(
        HistoryExportPlan plan, string directory, IProgress<HistoryExportProgress>? progress, CancellationToken token)
    {
        var result = new HistoryExportResult();
        result.Skipped.AddRange(plan.Skipped);
        var records = new List<HistoryExportRecord>();
        var processed = new HashSet<string>(StringComparer.Ordinal);
        var basename = $"SyncClipboard-history-{DateTime.Now:yyyyMMdd-HHmmss}";
        var partial = Path.Combine(directory, $".{basename}-{Guid.NewGuid():N}.partial");
        var ownsPartial = false;
        FileStream OpenOutput()
        {
            var stream = CreateOutput(partial);
            ownsPartial = true;
            return stream;
        }
        FileStream? output = null;
        ZipArchive? archive = null;
        long written = 0;
        var lastProgress = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            foreach (var item in plan.Items)
            {
                token.ThrowIfCancellationRequested();
                if (processed.Contains(item.ProfileId))
                    throw new InvalidDataException($"Duplicate Profile ID: {item.ProfileId}");
                FileHashInfo? transfer = null;
                FileStream? source = null;
                long sourceLength = 0;
                try
                {
                    ValidateSegment(item.ProfileId);
                    transfer = await PrepareTransferAsync(item, plan, token).ConfigureAwait(false);
                    if (transfer is not null)
                    {
                        source = new FileStream(transfer.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        // Validate the opened handle before creating a ZIP entry. Recheck bytes during the copy.
                        var actual = Convert.ToHexString(await SHA256.HashDataAsync(source, token).ConfigureAwait(false));
                        if (!Utility.SHA256Same(actual, transfer.Hash))
                            throw new InvalidDataException("Transfer data changed before export.");
                        source.Position = 0;
                        sourceLength = source.Length;
                    }
                }
                catch (Exception ex) when (!token.IsCancellationRequested && IsSourceError(ex))
                {
                    source?.Dispose();
                    result.Skipped.Add(new(item.ProfileId, item, Classify(ex), ex.Message));
                    processed.Add(item.ProfileId);
                    progress?.Report(new(processed.Count, plan.Items.Count, written));
                    continue;
                }
                catch
                {
                    source?.Dispose();
                    throw;
                }

                await using (source)
                {
                    HistoryExportTransferData? data = null;
                    if (source is not null && transfer is not null)
                    {
                        output ??= OpenOutput();
                        archive ??= new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                        var name = Path.GetFileName(transfer.Path);
                        var entryPath = $"files/{item.ProfileId}/{SafeFileName(name)}";
                        var entry = archive.CreateEntry(entryPath, CompressionLevel.NoCompression);
                        await using var destination = entry.Open();
                        void SourceFailed(Exception ex) =>
                            result.Skipped.Add(new(item.ProfileId, item, Classify(ex), ex.Message));
                        var copied = await CopyVerifiedAsync(source, destination, transfer.Hash, count =>
                        {
                            written += count;
                            if (lastProgress.ElapsedMilliseconds >= 100)
                            {
                                progress?.Report(new(processed.Count, plan.Items.Count, written));
                                lastProgress.Restart();
                            }
                        }, token, SourceFailed).ConfigureAwait(false);
                        if (copied != sourceLength)
                        {
                            var error = new InvalidDataException("Transfer data length changed during export.");
                            SourceFailed(error);
                            throw error;
                        }
                        data = new(name, entryPath, copied, transfer.Hash.ToUpperInvariant());
                    }
                    records.Add(ToRecord(item, data));
                    processed.Add(item.ProfileId);
                    progress?.Report(new(processed.Count, plan.Items.Count, written));
                }
            }

            token.ThrowIfCancellationRequested();
            if (records.Count > 0)
            {
                var document = new HistoryExportDocument("syncclipboard-history", 1, Env.AppVersion, DateTime.UtcNow, records);
                var extension = archive is null ? ".json" : ".zip";
                if (archive is not null)
                {
                    await using var json = archive.CreateEntry("history.json").Open();
                    await JsonSerializer.SerializeAsync(json, document, JsonOptions, token).ConfigureAwait(false);
                }
                else
                {
                    output = OpenOutput();
                    await JsonSerializer.SerializeAsync(output, document, JsonOptions, token).ConfigureAwait(false);
                }
                archive?.Dispose();
                archive = null;
                await output!.FlushAsync(token).ConfigureAwait(false);
                await output.DisposeAsync().ConfigureAwait(false);
                output = null;
                token.ThrowIfCancellationRequested();
                result.ArchivePath = Commit(partial, directory, basename, extension);
                result.ExportedCount = records.Count;
            }
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
                if (ownsPartial)
                    File.Delete(partial);
            }
            catch (Exception ex)
            {
                result.Error = $"{result.Error}\n{partial}: {ex.Message}";
            }
        }

        try
        {
            plan.Dispose();
        }
        catch (Exception ex)
        {
            result.Error = $"{result.Error}\n{ex.Message}".Trim();
        }

        if (result.ArchivePath is null && (result.Canceled || result.Error is not null))
        {
            var skipped = result.Skipped.Select(item => item.ProfileId).ToHashSet();
            foreach (var item in plan.Items.Where(item => !skipped.Contains(item.ProfileId)))
                result.Skipped.Add(new(item.ProfileId, item,
                    result.Canceled ? HistoryExportFailure.Canceled : HistoryExportFailure.ArchiveFailed, result.Error));
        }
        if (result.Skipped.Count > 0)
        {
            var reportBase = result.ArchivePath is { } path ? Path.GetFileNameWithoutExtension(path) : basename;
            await HistoryExportReportWriter.WriteAsync(result, directory, reportBase, profileEnv.GetHistoryPersistentDir()).ConfigureAwait(false);
        }
        return result;
    }

    private Profile CreateProfile(HistoryExportItem item) => Profile.Create(profileEnv.GetHistoryPersistentDir(), item.Content);

    private async Task<FileHashInfo?> PrepareTransferAsync(
        HistoryExportItem item, HistoryExportPlan plan, CancellationToken token)
    {
        Profile profile;
        try
        {
            profile = CreateProfile(item);
        }
        catch (NotSupportedException) when (Utility.IsValidSHA256(item.Content.TransferDataHash) && item.Content.TransferDataFile is not null)
        {
            var fallback = new FileHashInfo(Profile.GetFullPath(profileEnv.GetHistoryPersistentDir(), item.Content.Type,
                item.Content.Hash, item.Content.TransferDataFile), item.Content.TransferDataHash);
            var actual = await Utility.CalculateFileSHA256(fallback.Path, token).ConfigureAwait(false);
            if (!Utility.SHA256Same(actual, fallback.Hash))
                throw new InvalidDataException("Transfer data hash mismatch.");
            return fallback;
        }
        if (!await profile.IsDataComplete(true, token).ConfigureAwait(false))
            throw new FileNotFoundException("Local content is missing.");
        var transfer = await profile.PrepareTransferData(
            profileEnv.GetHistoryPersistentDir(), token, plan.TrackGeneratedFile).ConfigureAwait(false);
        if (profile.HasTransferData && transfer is null)
            throw new FileNotFoundException("Transfer data is missing.");
        return transfer;
    }

    private static HistoryExportRecord ToRecord(HistoryExportItem item, HistoryExportTransferData? data) => new(
        item.Content.Type.ToString(), item.Content.Hash.ToUpperInvariant(), item.Content.Text,
        item.Timestamp.ToUniversalTime(), item.LastModified.ToUniversalTime(), item.LastAccessed.ToUniversalTime(),
        item.Starred, item.Pinned, item.From, data is not null, data);

    internal static bool IsSourceError(Exception ex) => !IsDiskFull(ex) &&
        ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    private static bool IsDiskFull(Exception ex) =>
        ex is IOException && (ex.HResult & 0xFFFF) is 28 or 39 or 112 || ex.InnerException is { } inner && IsDiskFull(inner);

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

    private static string SafeFileName(string name)
    {
        var safe = new string(name.Select(c => c < ' ' || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray())
            .TrimEnd('.', ' ');
        var stem = safe.Split('.')[0].ToUpperInvariant();
        // Keep names usable when the archive is later extracted on Windows.
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')
            safe = "_" + safe;
        return safe.Length == 0 ? "data" : safe;
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
