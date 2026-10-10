using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History.HistoryExport;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.History.HistoryImport;

public sealed class HistoryImporter(IProfileEnv profileEnv, HistoryManager manager)
{
    private const long MaxManifestBytes = 64 * 1024 * 1024;
    private int _sessionActive;

    public IDisposable? TryBeginSession() => Interlocked.CompareExchange(ref _sessionActive, 1, 0) == 0
        ? new ScopeGuard(() => Interlocked.Exchange(ref _sessionActive, 0)) : null;

    public static async Task<HistoryImportPlan> PrepareAsync(string path, CancellationToken token)
    {
        var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        ZipArchive? archive = null;
        try
        {
            HistoryExportDocument? document;
            if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
                var manifest = archive.Entries.SingleOrDefault(entry => entry.FullName == "history.json")
                    ?? throw new InvalidDataException("history.json is missing.");
                if (manifest.Length > MaxManifestBytes)
                    throw new InvalidDataException("The backup manifest is too large.");
                await using var stream = manifest.Open();
                document = await ReadDocumentAsync(stream, token);
            }
            else if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            {
                if (source.Length > MaxManifestBytes)
                    throw new InvalidDataException("The backup manifest is too large.");
                document = await ReadDocumentAsync(source, token);
            }
            else
            {
                throw new InvalidDataException("Unsupported backup file.");
            }
            if (document is null || document.Format != "syncclipboard-history" || document.FormatVersion != 1 || document.Records is null)
                throw new InvalidDataException("Unsupported or invalid backup format.");
            return new(path, source, archive, document);
        }
        catch
        {
            archive?.Dispose();
            source.Dispose();
            throw;
        }
    }

    private static async Task<HistoryExportDocument?> ReadDocumentAsync(Stream stream, CancellationToken token)
    {
        // Bound decompression as well as the declared ZIP entry size.
        using var buffer = new MemoryStream();
        var bytes = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(bytes, token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > MaxManifestBytes)
                throw new InvalidDataException("The backup manifest is too large.");
            await buffer.WriteAsync(bytes.AsMemory(0, read), token).ConfigureAwait(false);
        }
        buffer.Position = 0;
        return await JsonSerializer.DeserializeAsync<HistoryExportDocument>(buffer, HistoryExporter.JsonOptions, token);
    }

    public async Task<HistoryImportResult> ImportAsync(
        HistoryImportPlan plan, IProgress<int>? progress, CancellationToken token)
    {
        var result = new HistoryImportResult();
        for (var i = 0; i < plan.Records.Count; i++)
        {
            if (token.IsCancellationRequested)
            {
                result.Canceled = true;
                break;
            }
            var item = plan.Records[i];
            string? directory = null;
            var saved = false;
            try
            {
                var type = ValidateRecord(item);
                if (await manager.ShouldSkipImportAsync(type, item.Hash, token).ConfigureAwait(false))
                {
                    result.ExistingCount++;
                    progress?.Report(i + 1);
                    continue;
                }
                var root = Profile.CreateWorkingDir(profileEnv.GetHistoryPersistentDir(), type, item.Hash.ToUpperInvariant());
                directory = Path.Combine(root, "import-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var record = await RestoreRecordAsync(plan, item, type, directory, token).ConfigureAwait(false);
                var outcome = await manager.ImportRecordAsync(record, directory, token).ConfigureAwait(false);
                saved = outcome != HistoryImportOutcome.Existing;
                switch (outcome)
                {
                    case HistoryImportOutcome.Imported: result.ImportedCount++; break;
                    case HistoryImportOutcome.Repaired: result.RepairedCount++; break;
                    default: result.ExistingCount++; break;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                result.Canceled = true;
                break;
            }
            catch (Exception ex)
            {
                result.Failures.Add(new(i + 1, $"{item?.Type}-{item?.Hash}", ex.Message));
                if (FileSys.IsDiskFull(ex))
                {
                    result.Error = ex.Message;
                    break;
                }
            }
            finally
            {
                if (!saved && directory is not null)
                {
                    try
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            progress?.Report(i + 1);
        }
        if (result.Failures.Count > 0)
            await HistoryImportReportWriter.WriteAsync(plan.Path, result).ConfigureAwait(false);
        return result;
    }

    private static ProfileType ValidateRecord(HistoryExportRecord? item)
    {
        if (item is null || !Enum.TryParse<ProfileType>(item.Type, out var type) ||
            type is not (ProfileType.Text or ProfileType.File or ProfileType.Image or ProfileType.Group))
            throw new InvalidDataException("Unsupported record type.");
        if (!Utility.IsValidSHA256(item.Hash) || item.Text is null || item.From is null || item.Size < 0)
            throw new InvalidDataException("Invalid record metadata.");
        if (type != ProfileType.Text && item.TransferData is null)
            throw new InvalidDataException("Transfer data is missing.");
        return type;
    }

    private async Task<HistoryRecord> RestoreRecordAsync(
        HistoryImportPlan plan, HistoryExportRecord item, ProfileType type, string directory, CancellationToken token)
    {
        var transfer = item.TransferData;
        var dto = new ProfileDto
        {
            Type = type,
            Hash = item.Hash.ToUpperInvariant(),
            Text = item.Text,
            Size = null,
            HasData = transfer is not null,
            DataName = transfer?.Name,
            TransferDataHash = transfer?.Sha256
        };
        Profile profile = type switch
        {
            ProfileType.Text => new TextProfile(dto),
            ProfileType.File => new FileProfile(dto),
            ProfileType.Image => new ImageProfile(dto),
            ProfileType.Group => new GroupProfile(dto),
            _ => throw new NotSupportedException()
        };
        if (transfer is not null)
        {
            var path = await RestoreTransferAsync(plan, item, directory, token).ConfigureAwait(false);
            var file = new FileHashInfo(path, transfer.Sha256);
            if (profile is GroupProfile group)
                await group.SetTransferData(file, item.Size, token).ConfigureAwait(false);
            else
                await profile.SetTransferData(file, verify: true, token).ConfigureAwait(false);
        }
        if (!await profile.IsDataComplete(false, token).ConfigureAwait(false))
            throw new InvalidDataException("Record content does not match its hash.");
        if (await profile.GetSize(token).ConfigureAwait(false) != item.Size)
            throw new InvalidDataException("Record size does not match its content.");
        var data = await profile.Persist(profileEnv.GetHistoryPersistentDir(), token).ConfigureAwait(false);
        return new HistoryRecord
        {
            Type = type,
            Hash = dto.Hash,
            Text = item.Text,
            Size = item.Size,
            FilePath = data.FilePaths,
            TransferDataFile = data.TransferDataFile,
            TransferDataHash = data.TransferDataHash,
            Timestamp = item.Timestamp.ToUniversalTime(),
            LastModified = item.LastModified.ToUniversalTime(),
            LastAccessed = item.LastAccessed.ToUniversalTime(),
            Stared = item.Starred,
            Pinned = item.Pinned,
            From = item.From,
            IsLocalFileReady = true,
            SyncStatus = HistorySyncStatus.LocalOnly
        };
    }

    private static async Task<string> RestoreTransferAsync(
        HistoryImportPlan plan, HistoryExportRecord item, string directory, CancellationToken token)
    {
        var data = item.TransferData!;
        if (string.IsNullOrWhiteSpace(data.Name) || data.Name is "." or ".." ||
            data.Name.IndexOfAny(['/', '\\']) >= 0 || data.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            data.Size < 0 || !Utility.IsValidSHA256(data.Sha256))
            throw new InvalidDataException("Invalid transfer metadata.");
        var expectedPath = $"files/{item.ProfileId}/{FileSys.SafeFileName(data.Name)}";
        if (data.Path != expectedPath || plan.Archive is null)
            throw new InvalidDataException("Invalid transfer path.");
        var entry = plan.Archive.Entries.SingleOrDefault(entry => entry.FullName == data.Path)
            ?? throw new FileNotFoundException("Transfer data is missing.", data.Path);
        if (entry.Length != data.Size)
            throw new InvalidDataException("Transfer size mismatch.");
        var path = Path.Combine(directory, data.Name);
        await using (var source = entry.Open())
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await CopyTransferAsync(source, output, data, token).ConfigureAwait(false);
        }
        return path;
    }

    private static async Task CopyTransferAsync(Stream source, Stream output, HistoryExportTransferData data, CancellationToken token)
    {
        var buffer = new byte[81920];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > data.Size)
                throw new InvalidDataException("Transfer size mismatch.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        if (total != data.Size || !Utility.SHA256Same(Convert.ToHexString(hash.GetHashAndReset()), data.Sha256))
            throw new InvalidDataException("Transfer size or SHA-256 mismatch.");
    }
}
