using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History.HistoryExport;
using SyncClipboard.Core.Utilities.History.HistoryImport;

namespace SyncClipboard.Core.Utilities.History;

public partial class HistoryManager
{
    private int _backupSessionActive;

    public IDisposable? TryBeginBackupSession()
    {
        if (Interlocked.CompareExchange(ref _backupSessionActive, 1, 0) != 0)
            return null;
        var released = 0;
        return new ScopeGuard(() =>
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                Interlocked.Exchange(ref _backupSessionActive, 0);
        });
    }

    public async Task<(bool Reused, string Hash)> TryReuseExistingRecordForImportAsync(
        ProfileType type, string hash, CancellationToken token)
    {
        HistoryRecord? existing;
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
        using (new ScopeGuard(() => _dbSemaphore.Release()))
        {
            existing = await Query(type, hash, token).ConfigureAwait(false);
            if (existing is null || !await CanReuseLocalContentAsync(existing, token).ConfigureAwait(false))
                return (false, existing?.Hash ?? hash.ToUpperInvariant());
            await UpdateExistingRecordAsync(existing, token).ConfigureAwait(false);
        }
        NotifyImportChange(existing, added: false);
        return (true, existing.Hash);
    }

    private async Task<bool> CanReuseLocalContentAsync(HistoryRecord record, CancellationToken token)
    {
        try
        {
            var profile = Profile.Create(_profileEnv.GetHistoryPersistentDir(), new ProfilePersistentInfo
            {
                Type = record.Type,
                Hash = record.Hash,
                Text = record.Text,
                Size = record.Size,
                FilePaths = record.FilePath,
                TransferDataFile = record.TransferDataFile,
                TransferDataHash = record.TransferDataHash
            });
            return !profile.HasTransferData || await profile.IsDataComplete(false, token).ConfigureAwait(false);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<HistoryImportOutcome> AddImportProfile(HistoryRecord record, string? importDirectory, CancellationToken token)
    {
        HistoryImportOutcome outcome;
        HistoryRecord changed;
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
        using (new ScopeGuard(() => _dbSemaphore.Release()))
        {
            var existing = await Query(record.Type, record.Hash, token).ConfigureAwait(false);
            changed = existing ?? record;
            if (existing is not null && await CanReuseLocalContentAsync(existing, token).ConfigureAwait(false))
            {
                await UpdateExistingRecordAsync(existing, token).ConfigureAwait(false);
                outcome = HistoryImportOutcome.Existing;
            }
            else
            {
                // The attachment directory was chosen before restoration; do not bind it to a different root.
                if (record.Hash != (existing?.Hash ?? record.Hash.ToUpperInvariant()))
                    throw new InvalidOperationException("History record changed during import. Please retry.");
                if (importDirectory is not null && !await CanReuseLocalContentAsync(record, token).ConfigureAwait(false))
                    throw new FileNotFoundException("Import attachments are no longer available or valid.");
                outcome = existing is null
                    ? await AddImportedRecordAsync(record, token).ConfigureAwait(false)
                    : await RepairExistingRecordDataAsync(existing, record, token).ConfigureAwait(false);
            }
        }
        NotifyImportChange(changed, outcome == HistoryImportOutcome.Imported);
        return outcome;
    }

    private async Task UpdateExistingRecordAsync(HistoryRecord record, CancellationToken token)
    {
        UpdateImportedRecordState(record);
        await _dbContext.SaveChangesAsync(token).ConfigureAwait(false);
    }

    private static void UpdateImportedRecordState(HistoryRecord record)
    {
        record.IsDeleted = false;
        record.IsLocalFileReady = true;
        record.LastModified = record.LastAccessed = DateTime.UtcNow;
        record.Version++;
        if (record.SyncStatus != HistorySyncStatus.LocalOnly)
            record.SyncStatus = HistorySyncStatus.NeedSync;
    }

    private void NotifyImportChange(HistoryRecord record, bool added)
    {
        var handlers = added ? HistoryAdded : HistoryUpdated;
        if (handlers is null)
            return;
        foreach (Action<HistoryRecord> handler in handlers.GetInvocationList().Cast<Action<HistoryRecord>>())
        {
            try
            {
                handler(record);
            }
            catch (Exception ex)
            {
                _logger.Write("HistoryManager", $"Import notification failed: {ex.Message}");
            }
        }
    }

    private async Task<HistoryImportOutcome> RepairExistingRecordDataAsync(
        HistoryRecord existing, HistoryRecord record, CancellationToken token)
    {
        var oldPaths = existing.FilePath;
        var oldFile = existing.TransferDataFile;
        var oldHash = existing.TransferDataHash;
        var oldReady = existing.IsLocalFileReady;
        try
        {
            existing.FilePath = record.FilePath;
            existing.TransferDataFile = record.TransferDataFile;
            existing.TransferDataHash = record.TransferDataHash;
            existing.IsLocalFileReady = true;
            UpdateImportedRecordState(existing);
            await _dbContext.SaveChangesAsync(token).ConfigureAwait(false);
        }
        catch
        {
            existing.FilePath = oldPaths;
            existing.TransferDataFile = oldFile;
            existing.TransferDataHash = oldHash;
            existing.IsLocalFileReady = oldReady;
            throw;
        }
        CleanupReplacedLocalFiles(existing, oldPaths, oldFile);
        return HistoryImportOutcome.Repaired;
    }

    private async Task<HistoryImportOutcome> AddImportedRecordAsync(HistoryRecord record, CancellationToken token)
    {
        record.Hash = record.Hash.ToUpperInvariant();
        record.ID = 0;
        record.IsDeleted = false;
        record.SyncStatus = HistorySyncStatus.LocalOnly;
        record.Version = 0;
        record.LastModified = record.LastAccessed = DateTime.UtcNow;
        _dbContext.HistoryRecords.Add(record);
        try
        {
            await _dbContext.SaveChangesAsync(token).ConfigureAwait(false);
        }
        catch
        {
            _dbContext.Entry(record).State = EntityState.Detached;
            throw;
        }
        return HistoryImportOutcome.Imported;
    }

    private void CleanupReplacedLocalFiles(HistoryRecord record, string[] oldPaths, string? oldTransfer)
    {
        try
        {
            var persistentDir = _profileEnv.GetHistoryPersistentDir();
            string Resolve(string path) => Profile.GetFullPath(persistentDir, record.Type, record.Hash, path);
            var previous = oldPaths.Concat(oldTransfer is null ? [] : [oldTransfer]).Select(Resolve);
            var current = record.FilePath.Concat(record.TransferDataFile is null ? [] : [record.TransferDataFile]).Select(Resolve);
            HistoryImportAttachmentCleanup.RemoveObsolete(
                Profile.QueryGetWorkingDir(persistentDir, record.Type, record.Hash), previous, current);
        }
        catch (Exception ex)
        {
            // The database update has already committed; cleanup must not turn it into a failed import.
            _logger.Write("HistoryManager", $"Obsolete import attachment cleanup failed: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<HistoryExportRecord>> GetExportSnapshotAsync(
        IReadOnlyList<HistoryRecordKey>? selected, CancellationToken token)
    {
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());
        if (selected is { Count: 0 })
            return [];
        var records = await _dbContext.HistoryRecords.AsNoTracking().Where(record => !record.IsDeleted)
            .ToListAsync(token).ConfigureAwait(false);
        var keys = selected?.Select(key => new HistoryRecordKey(key.Type, key.Hash.ToUpperInvariant())).ToHashSet();
        return records.Where(record => keys is null || keys.Contains(new(record.Type, record.Hash.ToUpperInvariant())))
            .Select(HistoryExportRecord.FromRecord).ToArray();
    }
}
