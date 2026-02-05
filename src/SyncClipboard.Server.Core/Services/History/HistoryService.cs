using Microsoft.EntityFrameworkCore;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Utilities.History;
using SyncClipboard.Shared.Utilities;
using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Hubs;
using System.Linq.Expressions;

namespace SyncClipboard.Server.Core.Services.History;

public class HistoryService : IHistoryEntityRepository<HistoryRecordEntity, DateTime>
{
    public const string HARD_CODED_USER_ID = "default_user";

    private readonly HistoryDbContext _dbContext;
    private readonly string _persistentDir;
    private readonly IHubContext<SyncClipboardHub, ISyncClipboardClient> _hubContext;
    private static readonly SemaphoreSlim _processSem = new(1, 1);
    private readonly SemaphoreSlim _sem;

    public HistoryService(HistoryDbContext dbContext,
        IProfileEnv profileEnv,
        IHubContext<SyncClipboardHub, ISyncClipboardClient> hubContext)
    {
        _dbContext = dbContext;
        _persistentDir = profileEnv.GetPersistentDir();
        _sem = _dbContext.Database.IsSqlite() ? _processSem : new SemaphoreSlim(1, 1);
        _hubContext = hubContext;
        _historyManagerHelper = new HistoryManagerHelper<HistoryRecordEntity, DateTime>(this);
    }

    public async Task<(bool? Updated, HistoryRecordDto? Server)> Update(
        string userId,
        ProfileType type,
        string hash,
        HistoryRecordUpdateDto dto,
        CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        hash = hash.ToUpperInvariant();
        var existing = await Query(userId, type, hash, token);
        if (existing is null)
        {
            return (null, null);
        }

        dto.Version ??= existing.Version + 1;
        dto.LastModified ??= DateTimeOffset.UtcNow;

        var shouldUpdate = HistoryHelper.ShouldUpdate(
            oldVersion: existing.Version,
            newVersion: dto.Version.Value,
            oldLastModified: new DateTimeOffset(existing.LastModified),
            newLastModified: dto.LastModified.Value);

        if (!shouldUpdate)
        {
            return (false, HistoryRecordDto.FromEntity(existing));
        }

        // 部分字段更新
        if (dto.Starred.HasValue) existing.Stared = dto.Starred.Value;
        if (dto.Pinned.HasValue) existing.Pinned = dto.Pinned.Value;
        if (dto.IsDelete.HasValue) existing.IsDeleted = dto.IsDelete.Value;

        existing.LastModified = dto.LastModified.Value.UtcDateTime;
        if (dto.LastAccessed.HasValue) existing.LastAccessed = dto.LastAccessed.Value.UtcDateTime;
        existing.Version = dto.Version.Value;

        await _dbContext.SaveChangesAsync(token);
        await NotifyProfileChangeAsync(existing);
        await DeleteProfileDataIfNeed(existing, token);

        return (true, HistoryRecordDto.FromEntity(existing));
    }

    private async Task NotifyProfileChangeAsync(HistoryRecordEntity entity)
    {
        if (_hubContext is null)
            return;

        try
        {
            var historyRecordDto = HistoryRecordDto.FromEntity(entity);
            await _hubContext.Clients.All.RemoteHistoryChanged(historyRecordDto);
        }
        catch { }
    }

    public async Task<List<HistoryRecordDto>> GetListAsync(
        string userId,
        int page,
        int pageSize,
        DateTime? before = null,
        DateTime? after = null,
        ProfileTypeFilter types = ProfileTypeFilter.All,
        string? searchText = null,
        bool? starred = null,
        DateTime? modifiedAfter = null,
        bool sortByLastAccessed = false,
        CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var query = _dbContext.HistoryRecords
            .Where(r => r.UserId == userId);

        if (after.HasValue)
        {
            var afterUtc = after.Value.ToUniversalTime();
            query = sortByLastAccessed
                ? query.Where(r => r.LastAccessed >= afterUtc)
                : query.Where(r => r.CreateTime >= afterUtc);
        }

        if (before.HasValue)
        {
            var beforeUtc = before.Value.ToUniversalTime();
            query = sortByLastAccessed
                ? query.Where(r => r.LastAccessed < beforeUtc)
                : query.Where(r => r.CreateTime < beforeUtc);
        }

        if (types != ProfileTypeFilter.All)
        {
            var includedTypes = Enum.GetValues(typeof(ProfileType))
                .Cast<ProfileType>()
                .Where(t => (types & (ProfileTypeFilter)(1 << (int)t)) != 0)
                .ToList();

            if (includedTypes.Count == 0)
            {
                return [];
            }

            query = query.Where(r => includedTypes.Contains(r.Type));
        }

        if (!string.IsNullOrEmpty(searchText))
        {
            query = query.Where(r => EF.Functions.Like(r.Text, $"%{searchText}%"));
        }

        if (starred.HasValue)
        {
            var flag = starred.Value;
            query = query.Where(r => r.Stared == flag);
        }

        if (modifiedAfter.HasValue)
        {
            var modifiedAfterUtc = modifiedAfter.Value.ToUniversalTime();
            query = query.Where(r => r.LastModified >= modifiedAfterUtc);
        }

        query = sortByLastAccessed
            ? query.OrderByDescending(r => r.LastAccessed).ThenByDescending(r => r.ID)
            : query.OrderByDescending(r => r.CreateTime).ThenByDescending(r => r.ID);

        var skip = (long)(page - 1) * pageSize;

        var list = await query
            .Skip((int)skip)
            .Take(pageSize)
            .ToListAsync(token);

        return list.Select(HistoryRecordDto.FromEntity).ToList();
    }

    public async Task AddProfile(string userId, Profile profile, CancellationToken token)
    {
        var entity = await profile.ToHistoryEntity(_persistentDir, userId, token);

        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var type = profile.Type;
        var hash = await profile.GetHash(token);

        var existing = await Query(userId, type, hash, token);
        if (existing is not null)
        {
            existing.LastAccessed = DateTime.UtcNow;
            existing.IsDeleted = false;
            existing.LastModified = DateTime.UtcNow;
            existing.TransferDataFile = entity.TransferDataFile;
            existing.FilePaths = entity.FilePaths;
            existing.Version++;
            await _dbContext.SaveChangesAsync(token);
            await NotifyProfileChangeAsync(existing);
            return;
        }

        await _dbContext.HistoryRecords.AddAsync(entity, token);
        await _dbContext.SaveChangesAsync(token);
        await NotifyProfileChangeAsync(entity);
    }

    public async Task<string?> GetRecentTransferFile(string userId, string fileName, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        var existing = _dbContext.HistoryRecords
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.LastAccessed)
            .AsEnumerable()
            .Where(r => Path.GetFileName(r.TransferDataFile) == fileName && File.Exists(Profile.GetFullPath(_persistentDir, r.Type, r.Hash, r.TransferDataFile)))
            .Select(r => Profile.GetFullPath(_persistentDir, r.Type, r.Hash, r.TransferDataFile))
            .FirstOrDefault();

        return existing;
    }

    public async Task<string?> GetTransferDataFileByProfileId(string userId, string profileId, CancellationToken token = default)
    {
        if (!Profile.ParseProfileId(profileId, out var type, out var hash) || string.IsNullOrEmpty(hash))
        {
            return null;
        }

        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var entity = await Query(userId, type, hash, token);
        if (entity is null)
        {
            return null;
        }

        var profile = entity.ToProfile(_persistentDir);
        return await profile.PrepareTransferData(_persistentDir, token);
    }

    private Task<HistoryRecordEntity?> Query(string userId, ProfileType type, string hash, CancellationToken token)
    {
        return _dbContext.HistoryRecords.FirstOrDefaultAsync(
            r => r.UserId == userId && EF.Functions.Like(r.Hash, hash) && r.Type == type, token);
    }

    public async Task<HistoryRecordDto?> GetByTypeAndHashAsync(string userId, ProfileType type, string hash, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var entity = await Query(userId, type, hash, token);
        if (entity is null)
        {
            return null;
        }

        return HistoryRecordDto.FromEntity(entity);
    }

    public async Task<Profile?> GetExistingProfileAsync(string userId, ProfileType type, string hash, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var entity = await Query(userId, type, hash, token);
        if (entity is null || entity.IsDeleted)
        {
            return null;
        }

        entity.LastModified = DateTime.UtcNow;
        entity.LastAccessed = DateTime.UtcNow;
        entity.Version++;
        await _dbContext.SaveChangesAsync(token);
        await NotifyProfileChangeAsync(entity);

        return entity.ToProfile(_persistentDir);
    }

    public async Task<HistoryRecordDto> AddRecordDto(
        string userId,
        HistoryRecordDto incoming,
        Stream? transferFileStream,
        CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var existing = await Query(userId, incoming.Type, incoming.Hash, token);

        if (existing is not null)
        {
            var shouldUpdate = HistoryHelper.ShouldUpdate(
                oldVersion: existing.Version,
                newVersion: incoming.Version,
                oldLastModified: new DateTimeOffset(existing.LastModified),
                newLastModified: incoming.LastModified);
            if (shouldUpdate)
            {
                if (existing.IsDeleted && transferFileStream != null)
                {
                    await SaveTransferDataAsync(existing, transferFileStream, token);
                }

                UpdateEntityFields(incoming.ToEntity(userId), existing);
                await _dbContext.SaveChangesAsync(token);
                await NotifyProfileChangeAsync(existing);
                await DeleteProfileDataIfNeed(existing, token);
            }

            return HistoryRecordDto.FromEntity(existing);
        }

        var entity = incoming.ToEntity(userId);

        if (transferFileStream != null)
        {
            var profile = await SaveTransferDataAsync(entity, transferFileStream, token);
            var newEntity = await profile.ToHistoryEntity(_persistentDir, userId, token);
            UpdateEntityFields(entity, newEntity);
            entity = newEntity;
        }

        await _dbContext.HistoryRecords.AddAsync(entity, token);
        await _dbContext.SaveChangesAsync(token);
        await NotifyProfileChangeAsync(entity);
        await DeleteProfileDataIfNeed(entity, token);
        return HistoryRecordDto.FromEntity(entity);
    }

    private static void UpdateEntityFields(HistoryRecordEntity srcEntity, HistoryRecordEntity dstEntity)
    {
        dstEntity.CreateTime = srcEntity.CreateTime;
        dstEntity.LastAccessed = srcEntity.LastAccessed;
        dstEntity.LastModified = srcEntity.LastModified;
        dstEntity.Stared = srcEntity.Stared;
        dstEntity.Pinned = srcEntity.Pinned;
        dstEntity.Version = srcEntity.Version;
        dstEntity.IsDeleted = srcEntity.IsDeleted;
    }

    private async Task<Profile> SaveTransferDataAsync(HistoryRecordEntity entity, Stream transferFileStream, CancellationToken token)
    {
        var profile = entity.ToProfile(_persistentDir);
        var filePath = await profile.NeedsTransferData(_persistentDir, token)
            ?? throw new InvalidOperationException("Profile does not support transfer data.");
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await transferFileStream.CopyToAsync(fs, token);
        }

        await profile.SetTransferData(filePath, verify: true, token);
        return profile;
    }

    private Task DeleteProfileDataIfNeed(HistoryRecordEntity entity, CancellationToken token)
    {
        return DeleteProfileData(entity, false, token);
    }

    private async Task DeleteProfileData(HistoryRecordEntity entity, bool force, CancellationToken token)
    {
        if (!force && entity.IsDeleted == false)
        {
            return;
        }

        var workingDir = Profile.QueryGetWorkingDir(_persistentDir, entity.Type, entity.Hash);
        await Task.Run(() =>
        {
            if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir))
            {
                try
                {
                    Directory.Delete(workingDir, true);
                }
                catch when (!token.IsCancellationRequested) { }
            }
        }, token);
    }

    // public async Task Remove(string userId, ProfileType type, string hash, CancellationToken token = default)
    // {
    //     await _sem.WaitAsync(token);
    //     using var guard = new ScopeGuard(() => _sem.Release());

    //     var existing = await Query(userId, type, hash, token);
    //     if (existing is null)
    //     {
    //         return;
    //     }

    //     existing.IsDeleted = true;
    //     _dbContext.HistoryRecords.Remove(existing);
    //     await _dbContext.SaveChangesAsync(token);
    //     await DeleteProfileData(existing, token);
    // }

    public async Task RemoveOutOfDateDeletedRecords(CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var toDeletes = _dbContext.HistoryRecords
            .Where(r => r.UserId == HARD_CODED_USER_ID && r.IsDeleted && r.LastModified < DateTime.UtcNow.AddDays(-30));

        if (!toDeletes.Any())
        {
            return;
        }

        _dbContext.HistoryRecords.RemoveRange(toDeletes);
        await _dbContext.SaveChangesAsync(token);

        foreach (var rec in toDeletes)
        {
            await DeleteProfileDataIfNeed(rec, token);
        }
    }

    public async Task<int> ClearAllAsync(string userId, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var records = await _dbContext.HistoryRecords
            .Where(r => r.UserId == userId)
            .ToListAsync(token);

        _dbContext.HistoryRecords.RemoveRange(records);
        var count = records.Count;
        await _dbContext.SaveChangesAsync(token);

        foreach (var rec in records)
        {
            await DeleteProfileData(rec, true, token);
        }

        return count;
    }

    # region IHistoryEntityRepository Implementation
    private readonly HistoryManagerHelper<HistoryRecordEntity, DateTime> _historyManagerHelper;

    public DbSet<HistoryRecordEntity> RecordDbSet => _dbContext.HistoryRecords;

    public Expression<Func<HistoryRecordEntity, bool>> QueryCount => entity => !entity.IsDeleted;
    public Expression<Func<HistoryRecordEntity, bool>> QueryToDeleteByOverCount => entity => !entity.Stared && !entity.Pinned && !entity.IsDeleted;
    public Expression<Func<HistoryRecordEntity, DateTime>> QueryDeleteOrderBy => entity => entity.LastAccessed;

    public async Task DeleteHistoryByOverCount(HistoryRecordEntity entity, CancellationToken token)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var existing = await Query(entity.UserId, entity.Type, entity.Hash, token);
        if (existing is null)
        {
            return;
        }

        existing.IsDeleted = true;
        existing.LastModified = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(token);
        await NotifyProfileChangeAsync(existing);
        await DeleteProfileDataIfNeed(existing, token);
    }

    public Task<uint> SetRecordsMaxCount(uint maxCount, CancellationToken token = default)
    {
        return _historyManagerHelper.SetRecordsMaxCount(maxCount, token);
    }
    #endregion

    public async Task<HistoryStatisticsDto> GetStatisticsAsync(string userId, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var records = await _dbContext.HistoryRecords
            .Where(r => r.UserId == userId)
            .ToListAsync(token);

        var totalCount = records.Count;
        var starredCount = records.Count(r => r.Stared);
        var deletedCount = records.Count(r => r.IsDeleted);
        var activeCount = records.Count(r => !r.IsDeleted);

        // 计算_persistentDir文件夹的总大小
        long totalFileSizeBytes = 0;
        if (Directory.Exists(_persistentDir))
        {
            try
            {
                var dirInfo = new DirectoryInfo(_persistentDir);
                totalFileSizeBytes = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(file => file.Length);
            }
            catch { }
        }

        // 转换为MB，保留两位小数
        double totalFileSizeMB = totalFileSizeBytes / (1024.0 * 1024.0);
        totalFileSizeMB = Math.Round(totalFileSizeMB, 2);

        // 如果不是0但是显示为0，则显示为0.01
        if (totalFileSizeBytes > 0 && totalFileSizeMB == 0)
        {
            totalFileSizeMB = 0.01;
        }

        return new HistoryStatisticsDto
        {
            TotalCount = totalCount,
            StarredCount = starredCount,
            DeletedCount = deletedCount,
            ActiveCount = activeCount,
            TotalFileSizeMB = totalFileSizeMB
        };
    }

    public async Task CleanOrphanedFolders(CancellationToken token = default)
    {
        if (!Directory.Exists(_persistentDir))
        {
            return;
        }

        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var directories = Directory.GetDirectories(_persistentDir);
        foreach (var directory in directories)
        {
            token.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(directory);
            var parts = dirName.Split('_', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            if (!Enum.TryParse<ProfileType>(parts[0], out var type))
            {
                continue;
            }

            var hash = parts[1];
            var entity = await Query(HARD_CODED_USER_ID, type, hash, token);

            if (entity is null || entity.IsDeleted)
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
