using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History.HistoryExport;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.History.HistoryImport;

public sealed class HistoryImporter
{
    private readonly IProfileEnv profileEnv;
    private readonly HistoryManager manager;
    private readonly Func<string, long> availableSpace;

    public HistoryImporter(IProfileEnv profileEnv, HistoryManager manager)
        : this(profileEnv, manager, path => new DriveInfo(path).AvailableFreeSpace) { }

    internal HistoryImporter(
        IProfileEnv profileEnv, HistoryManager manager, Func<string, long> availableSpace)
    {
        this.profileEnv = profileEnv;
        this.manager = manager;
        this.availableSpace = availableSpace;
    }

    private const long MaxManifestBytes = 64 * 1024 * 1024;

    public static async Task<HistoryImportPlan> PrepareAsync(string path, CancellationToken token)
    {
        var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        ZipArchive? archive = null;
        try
        {
            HistoryImportDocument? document;
            if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
                document = await ReadArchiveDocumentAsync(archive, token);
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
            ValidateDocument(document);
            return new(path, source, archive, document, token);
        }
        catch
        {
            archive?.Dispose();
            source.Dispose();
            throw;
        }
    }

    private static async Task<HistoryImportDocument?> ReadArchiveDocumentAsync(ZipArchive archive, CancellationToken token)
    {
        ZipArchiveEntry? manifest = null;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.FullName != "history.json")
                continue;
            if (manifest is not null)
                throw new InvalidDataException("Duplicate history.json.");
            manifest = entry;
        }
        if (manifest is null)
            throw new InvalidDataException("history.json is missing.");
        if (manifest.Length > MaxManifestBytes)
            throw new InvalidDataException("The backup manifest is too large.");
        await using var stream = manifest.Open();
        return await ReadDocumentAsync(stream, token);
    }

    private static void ValidateDocument([NotNull] HistoryImportDocument? document)
    {
        if (document is null || document.Format != "syncclipboard-history" || document.FormatVersion != 1 || document.Records.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Unsupported or invalid backup format.");
    }

    private static async Task<HistoryImportDocument?> ReadDocumentAsync(Stream stream, CancellationToken token)
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
        return await JsonSerializer.DeserializeAsync<HistoryImportDocument>(buffer, HistoryExporter.JsonOptions, token);
    }

    public async Task<HistoryImportResult> ImportAsync(
        HistoryImportPlan plan, IProgress<int>? progress, CancellationToken token)
    {
        var result = new HistoryImportResult();
        IDisposable cleanupPause;
        try
        {
            cleanupPause = await manager.PauseCleanupAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            result.Canceled = true;
            return result;
        }
        using var pause = cleanupPause;
        await using var report = new HistoryImportReportWriter(plan.Path, result);
        for (var i = 0; i < plan.Records.Count; i++)
        {
            if (token.IsCancellationRequested)
            {
                result.Canceled = true;
                break;
            }
            HistoryExportRecord? item = null;
            try
            {
                item = plan.Records[i];
                var outcome = await ImportItemAsync(plan, item, token).ConfigureAwait(false);
                CountOutcome(result, outcome);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                result.Canceled = true;
                break;
            }
            catch (Exception ex)
            {
                result.FailureCount++;
                if (FileSys.IsDiskFull(ex))
                    result.Error = ex.Message;
                var reported = await report.AppendAsync(new(i + 1, $"{item?.Type}-{item?.Hash}", ex.Message), token).ConfigureAwait(false);
                if (!reported || result.Error is not null)
                    break;
            }
            progress?.Report(i + 1);
        }
        await report.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    private async Task<HistoryImportOutcome> ImportItemAsync(
        HistoryImportPlan plan, HistoryExportRecord? item, CancellationToken token)
    {
        var type = ValidateRecord(item);
        var (reused, hash) = await manager.TryReuseExistingRecordForImportAsync(type, item.Hash, token).ConfigureAwait(false);
        if (reused)
            return HistoryImportOutcome.Existing;

        string? directory = null;
        string? createdRoot = null;
        var saved = false;
        try
        {
            if (item.TransferData is not null)
            {
                var root = Profile.QueryGetWorkingDir(profileEnv.GetHistoryPersistentDir(), type, hash);
                if (!Directory.Exists(root))
                    createdRoot = root;
                directory = Path.Combine(root, "import-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
            }
            var record = await RestoreRecordAsync(plan, item, type, hash, directory, token).ConfigureAwait(false);
            var outcome = await manager.AddImportProfile(record, directory, token).ConfigureAwait(false);
            saved = outcome != HistoryImportOutcome.Existing;
            return outcome;
        }
        finally
        {
            if (!saved)
                TryDeleteDirectory(directory, recursive: true);
            TryDeleteDirectory(createdRoot, recursive: false);
        }
    }

    private static void CountOutcome(HistoryImportResult result, HistoryImportOutcome outcome)
    {
        switch (outcome)
        {
            case HistoryImportOutcome.Imported: result.ImportedCount++; break;
            case HistoryImportOutcome.Repaired: result.RepairedCount++; break;
            default: result.ExistingCount++; break;
        }
    }

    private static void TryDeleteDirectory(string? path, bool recursive)
    {
        if (path is null)
            return;
        try
        {
            Directory.Delete(path, recursive);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static ProfileType ValidateRecord([NotNull] HistoryExportRecord? item)
    {
        var type = ValidateRecordType(item);
        ValidateRecordMetadata(item);
        ValidateRecordTimestamps(item);
        if (type != ProfileType.Text && item.TransferData is null)
            throw new InvalidDataException("Transfer data is missing.");
        return type;
    }

    private static ProfileType ValidateRecordType([NotNull] HistoryExportRecord? item)
    {
        if (item is null || !Enum.TryParse<ProfileType>(item.Type, out var type) ||
            type is not (ProfileType.Text or ProfileType.File or ProfileType.Image or ProfileType.Group))
            throw new InvalidDataException("Unsupported record type.");
        return type;
    }

    private static void ValidateRecordMetadata(HistoryExportRecord item)
    {
        if (!Utility.IsValidSHA256(item.Hash) || item.Text is null || item.From is null || item.Size < 0)
            throw new InvalidDataException("Invalid record metadata.");
    }

    private static void ValidateRecordTimestamps(HistoryExportRecord item)
    {
        if (!IsValidTimestamp(item.Timestamp) || !IsValidTimestamp(item.LastModified) || !IsValidTimestamp(item.LastAccessed))
            throw new InvalidDataException("Missing or invalid record timestamp.");
    }

    private static bool IsValidTimestamp(DateTime value)
    {
        if (value == DateTime.MinValue || value == DateTime.MaxValue)
            return false;
        var utc = value.ToUniversalTime();
        return utc != DateTime.MinValue && utc != DateTime.MaxValue;
    }

    private async Task<HistoryRecord> RestoreRecordAsync(
        HistoryImportPlan plan, HistoryExportRecord item, ProfileType type, string hash, string? directory, CancellationToken token)
    {
        var profile = CreateImportProfile(item, type, hash);
        await RestoreProfileTransferAsync(plan, item, profile, directory, token).ConfigureAwait(false);
        await ValidateRestoredProfileAsync(profile, item, directory, token).ConfigureAwait(false);
        var data = await profile.Persist(profileEnv.GetHistoryPersistentDir(), token).ConfigureAwait(false);
        return new HistoryRecord
        {
            Type = type,
            Hash = hash,
            Text = item.Text,
            Size = data.Size,
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

    private static Profile CreateImportProfile(HistoryExportRecord item, ProfileType type, string hash)
    {
        var transfer = item.TransferData;
        var dto = new ProfileDto
        {
            Type = type,
            Hash = hash,
            Text = type == ProfileType.Group ? "" : item.Text,
            Size = null,
            HasData = transfer is not null,
            DataName = transfer?.Name,
            TransferDataHash = transfer?.Sha256
        };
        return type switch
        {
            ProfileType.Text => new TextProfile(dto),
            ProfileType.File => new FileProfile(dto),
            ProfileType.Image => new ImageProfile(dto),
            ProfileType.Group => new GroupProfile(dto),
            _ => throw new NotSupportedException()
        };
    }

    private async Task RestoreProfileTransferAsync(
        HistoryImportPlan plan, HistoryExportRecord item, Profile profile, string? directory, CancellationToken token)
    {
        var transfer = item.TransferData;
        if (transfer is not null)
        {
            var path = await RestoreTransferAsync(plan, item, directory!, token).ConfigureAwait(false);
            var file = new FileHashInfo(path, transfer.Sha256);
            await profile.SetTransferData(file, verify: true, token).ConfigureAwait(false);
        }
    }

    private static async Task ValidateRestoredProfileAsync(
        Profile profile, HistoryExportRecord item, string? directory, CancellationToken token)
    {
        if (!await profile.IsDataComplete(false, token).ConfigureAwait(false))
            throw new InvalidDataException("Record content does not match its hash.");
        if (profile is TextProfile && item.TransferData is not null)
        {
            await ValidateDecodedTextAsync(profile, item, directory!, token).ConfigureAwait(false);
        }
    }

    private static async Task ValidateDecodedTextAsync(
        Profile profile, HistoryExportRecord item, string directory, CancellationToken token)
    {
        var content = await profile.Localize(directory!, token).ConfigureAwait(false);
        var decodedProfile = new TextProfile(content.Text);
        if (!Utility.SHA256Same(await decodedProfile.GetHash(token).ConfigureAwait(false), item.Hash))
            throw new InvalidDataException("Decoded text does not match its hash.");
    }

    private async Task<string> RestoreTransferAsync(
        HistoryImportPlan plan, HistoryExportRecord item, string directory, CancellationToken token)
    {
        var data = item.TransferData!;
        if (string.IsNullOrEmpty(data.Name) || data.Name is "." or ".." ||
            data.Name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 ||
            data.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            data.Size < 0 || !Utility.IsValidSHA256(data.Sha256))
            throw new InvalidDataException("Invalid transfer metadata.");
        var expectedPath = $"files/{item.ProfileId}/{FileSys.SafeFileName(data.Name)}";
        if (data.Path != expectedPath)
            throw new InvalidDataException("Invalid transfer path.");
        var entry = plan.GetTransferEntry(data.Path);
        if (entry.Length != data.Size)
            throw new InvalidDataException("Transfer size mismatch.");
        var requiredSpace = item.ProfileType == ProfileType.Group ? checked(data.Size + item.Size) : data.Size;
        FileSys.EnsureAvailableSpace(requiredSpace, availableSpace(directory));
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
