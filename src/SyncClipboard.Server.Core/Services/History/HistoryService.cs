using Microsoft.EntityFrameworkCore;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Utilities.History;
using SyncClipboard.Shared.Utilities;
using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Hubs;

namespace SyncClipboard.Server.Core.Services.History;

public class HistoryService
{
    public const string HARD_CODED_USER_ID = "default_user";

    private readonly HistoryDbContext _dbContext;
    private readonly string _persistentDir;
    private readonly IHubContext<SyncClipboardHub, ISyncClipboardClient> _hubContext;

    // When using SQLite provider we need a process-wide semaphore to avoid concurrent write issues.
    private static readonly SemaphoreSlim _processSem = new(1, 1);
    // Per-instance semaphore used when not using SQLite
    private readonly SemaphoreSlim _sem;

    public HistoryService(HistoryDbContext dbContext, IProfileEnv profileEnv, IHubContext<SyncClipboardHub, ISyncClipboardClient> hubContext)
    {
        _dbContext = dbContext;
        _persistentDir = profileEnv.GetPersistentDir();
        _sem = _dbContext.Database.IsSqlite() ? _processSem : new SemaphoreSlim(1, 1);
        _hubContext = hubContext;
    }

    // public async Task<HistoryRecordDto?> GetAsync(string userId, string hash, ProfileType type, CancellationToken token = default)
    // {
    //     await _sem.WaitAsync(token);
    //     try
    //     {
    //         var entity = await _dbContext.HistoryRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Hash == hash && r.Type == type, token);
    //         if (entity == null) return null;

    //         entity.LastAccessed = DateTime.UtcNow;
    //         await _dbContext.SaveChangesAsync(token);
    //         return HistoryRecordDto.FromEntity(entity);
    //     }
    //     finally
    //     {
    //         _sem.Release();
    //     }
    // }

    public async Task<(bool? Updated, HistoryRecordDto? Server)> UpdateWithConcurrencyAsync(
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
            return (null, null); // not found
        }

        dto.Version ??= existing.Version + 1;
        dto.LastModified ??= DateTimeOffset.UtcNow;
        // 并发检测：使用 ShouldUpdate 综合判断（5分钟阈值内用版本，超出用时间戳）
        // 将可空的客户端值回退为已有值以满足非可空签名
        var shouldUpdate = HistoryHelper.ShouldUpdate(
            oldVersion: existing.Version,
            newVersion: dto.Version.Value,
            oldLastModified: AsUtcOffset(existing.LastModified),
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
        existing.Version = dto.Version.Value;

        await _dbContext.SaveChangesAsync(token);

        // Notify clients of profile change
        await NotifyProfileChangeAsync(existing);

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

    private static DateTimeOffset AsUtcOffset(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Utc)
        {
            return new DateTimeOffset(dt);
        }
        // Treat non-UTC as UTC-equivalent by converting to UTC first
        return new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero);
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
            query = query.Where(r => r.CreateTime >= afterUtc);
        }

        if (before.HasValue)
        {
            var beforeUtc = before.Value.ToUniversalTime();
            query = query.Where(r => r.CreateTime < beforeUtc);
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

        // Filter by starred if provided
        if (starred.HasValue)
        {
            var flag = starred.Value;
            query = query.Where(r => r.Stared == flag);
        }

        query = query.OrderByDescending(r => r.CreateTime)
                     .ThenByDescending(r => r.ID);

        var skip = (long)(page - 1) * pageSize;

        var list = await query
            .Skip((int)skip)
            .Take(pageSize)
            .ToListAsync(token);

        return list.Select(HistoryRecordDto.FromEntity).ToList();
    }

    public async Task AddProfile(string userId, Profile profile, CancellationToken token)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var type = profile.Type;
        var hash = await profile.GetHash(token);

        var existing = await Query(userId, type, hash, token);
        if (existing is not null)
        {
            existing.LastAccessed = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(token);
            return;
        }

        var entity = await profile.ToHistoryEntity(_persistentDir, userId, token);
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
            .Where(r => r.UserId == userId && r.Type != ProfileType.Text)
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

        entity.LastAccessed = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(token);
        await NotifyProfileChangeAsync(entity);
        return HistoryRecordDto.FromEntity(entity);
    }

    public async Task<Profile?> GetProfileAsync(string userId, ProfileType type, string hash, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var entity = await Query(userId, type, hash, token);
        if (entity is null)
        {
            return null;
        }

        return entity.ToProfile(_persistentDir);
    }

    /// <summary>
    /// 如果记录不存在则创建，存在则根据并发规则尝试更新。返回服务器端快照。
    /// </summary>
    public async Task<HistoryRecordDto> AddRecordDto(
        string userId,
        HistoryRecordDto incoming,
        Stream? transferFileStream,
        CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var existing = await Query(userId, incoming.Type, incoming.Hash, token);
        var now = DateTime.UtcNow;

        if (existing is not null)
        {
            var shouldUpdate = HistoryHelper.ShouldUpdate(
                oldVersion: existing.Version,
                newVersion: incoming.Version,
                oldLastModified: AsUtcOffset(existing.LastModified),
                newLastModified: incoming.LastModified);

            if (shouldUpdate)
            {
                existing.Stared = incoming.Starred;
                existing.Pinned = incoming.Pinned;
                existing.IsDeleted = incoming.IsDeleted;
                existing.LastModified = incoming.LastModified.UtcDateTime;
                existing.Version = incoming.Version;

                await _dbContext.SaveChangesAsync(token);
                await NotifyProfileChangeAsync(existing);
            }

            return HistoryRecordDto.FromEntity(existing);
        }

        var entity = new HistoryRecordEntity
        {
            UserId = userId,
            Type = incoming.Type,
            Hash = incoming.Hash.ToUpperInvariant(),
            Text = incoming.Text,
            Size = 0,
            CreateTime = incoming.CreateTime.UtcDateTime,
            LastAccessed = now,
            LastModified = (incoming.LastModified == default ? DateTimeOffset.UtcNow : incoming.LastModified).UtcDateTime,
            Stared = incoming.Starred,
            Pinned = incoming.Pinned,
            Version = incoming.Version,
            IsDeleted = incoming.IsDeleted,
        };

        var profile = entity.ToProfile(_persistentDir);

        if (transferFileStream != null)
        {
            if (!profile.NeedsTransferData(_persistentDir, out var filePath))
            {
                throw new InvalidOperationException("Profile does not support transfer data.");
            }

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await transferFileStream.CopyToAsync(fs, token);
            }

            await profile.SetTranseferData(filePath, verify: true, token);
            entity = await profile.ToHistoryEntity(_persistentDir, userId, token);
        }

        await _dbContext.HistoryRecords.AddAsync(entity, token);
        await _dbContext.SaveChangesAsync(token);

        // Notify clients of new profile
        await NotifyProfileChangeAsync(entity);

        return HistoryRecordDto.FromEntity(entity);
    }

    /// <summary>
    /// 删除指定用户的全部历史记录以及相关的传输数据文件与目录。
    /// 返回删除的记录数量。
    /// </summary>
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
            if (!string.IsNullOrEmpty(rec.TransferDataFile))
            {
                try
                {
                    var dir = Path.GetDirectoryName(rec.TransferDataFile);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        Directory.Delete(dir, true);
                    }
                }
                catch
                {
                }
            }
        }

        return count;
    }
}
