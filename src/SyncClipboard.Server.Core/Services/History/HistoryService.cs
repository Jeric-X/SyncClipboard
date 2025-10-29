using Microsoft.EntityFrameworkCore;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Utilities.History;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Server.Core.Services.History;

public class HistoryService
{
    public const string HARD_CODED_USER_ID = "default_user";
    public string HistoryDataFolder => Path.Combine(_env.WebRootPath, "history");

    private readonly HistoryDbContext _dbContext;
    private readonly IWebHostEnvironment _env;
    // When using SQLite provider we need a process-wide semaphore to avoid concurrent write issues.
    private static readonly SemaphoreSlim _processSem = new(1, 1);
    // Per-instance semaphore used when not using SQLite
    private readonly SemaphoreSlim _sem;

    public HistoryService(HistoryDbContext dbContext, IWebHostEnvironment env)
    {
        _dbContext = dbContext;
        _env = env;
        _sem = _dbContext.Database.IsSqlite() ? _processSem : new SemaphoreSlim(1, 1);
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

    public async Task<List<HistoryRecordDto>> GetListAsync(string userId, ProfileType type, int page = 1, int pageSize = 50, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        // Enforce maximum page size of 50
        const int MAX_PAGE_SIZE = 50;
        if (pageSize > MAX_PAGE_SIZE) pageSize = MAX_PAGE_SIZE;

        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var query = _dbContext.HistoryRecords
            .Where(r => r.UserId == userId && (type == ProfileType.None || r.Type == type))
            .OrderByDescending(r => r.CreateTime)
            .ThenBy(r => r.ID);

        var skip = (long)(page - 1) * pageSize;

        var list = await query
            .Skip((int)skip)
            .Take(pageSize)
            .ToListAsync(token);

        return list.Select(HistoryRecordDto.FromEntity).ToList();
    }

    // public async Task SetAsync(string userId, HistoryRecordEntity record, CancellationToken token = default)
    // {
    //     await _sem.WaitAsync(token);
    //     try
    //     {
    //         var existing = await _dbContext.HistoryRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Hash == record.Hash && r.Type == record.Type, token);
    //         if (existing != null)
    //         {
    //             // Update fields
    //             existing.Text = record.Text;
    //             existing.FilePathJson = record.FilePathJson;
    //             existing.CreateTime = record.CreateTime;
    //             existing.Stared = record.Stared;
    //             existing.Pinned = record.Pinned;
    //             existing.Size = record.Size;
    //             existing.LastAccessed = DateTime.UtcNow;
    //         }
    //         else
    //         {
    //             record.UserId = userId;
    //             record.LastAccessed = DateTime.UtcNow;
    //             await _dbContext.HistoryRecords.AddAsync(record, token);
    //         }

    //         await _dbContext.SaveChangesAsync(token);
    //     }
    //     finally
    //     {
    //         _sem.Release();
    //     }
    // }

    // public async Task SetWithDataAsync(string userId, string hash, ProfileType type, Stream? data, string? fileName = null, CancellationToken token = default)
    // {
    //     await _sem.WaitAsync(token);
    //     try
    //     {
    //         var existing = await _dbContext.HistoryRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Hash == hash && r.Type == type, token);

    //         if (existing != null)
    //         {
    //             // existing record: only update LastAccessed and return. Do not accept/overwrite data in this flow.
    //             existing.LastAccessed = DateTime.UtcNow;
    //             await _dbContext.SaveChangesAsync(token);
    //             return;
    //         }
    //         else
    //         {
    //             var record = new HistoryRecordEntity
    //             {
    //                 UserId = userId,
    //                 Hash = hash,
    //                 Type = type,
    //                 Text = string.Empty,
    //                 Stared = false,
    //                 Pinned = false,
    //                 // Mime left null unless set by other flows
    //                 CreateTime = DateTime.UtcNow,
    //                 LastAccessed = DateTime.UtcNow
    //             };

    //             await _dbContext.HistoryRecords.AddAsync(record, token);
    //             existing = record;
    //         }

    //         // Handle data stream: store under WebRootPath/data/files/{userId}/{hash}/{fileName}
    //         if (data != null)
    //         {
    //             var webRoot = string.IsNullOrEmpty(_env.WebRootPath) ? _env.ContentRootPath : _env.WebRootPath;
    //             var baseFolder = Path.Combine(webRoot, "data", "files", userId, hash);
    //             if (!Directory.Exists(baseFolder)) Directory.CreateDirectory(baseFolder);

    //             var safeFileName = string.IsNullOrEmpty(fileName) ? "data.bin" : fileName;
    //             var filePath = Path.Combine(baseFolder, safeFileName);

    //             // Write stream to file
    //             using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
    //             {
    //                 await data.CopyToAsync(fs, token);
    //             }

    //             // update metadata
    //             existing.FilePath = [filePath];
    //             existing.Size = new FileInfo(filePath).Length;
    //         }

    //         await _dbContext.SaveChangesAsync(token);
    //     }
    //     finally
    //     {
    //         _sem.Release();
    //     }
    // }

    // public async Task<bool> UpdateAsync(string userId, string hash, ProfileType type, HistoryRecordUpdateDto dto, CancellationToken token = default)
    // {
    //     await _sem.WaitAsync(token);
    //     try
    //     {
    //         var existing = await _dbContext.HistoryRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Hash == hash && r.Type == type, token);
    //         if (existing == null) return false;

    //         if (dto.Text is not null) existing.Text = dto.Text;
    //         if (dto.Stared.HasValue) existing.Stared = dto.Stared.Value;
    //         if (dto.Pinned.HasValue) existing.Pinned = dto.Pinned.Value;

    //         existing.LastAccessed = DateTime.UtcNow;
    //         await _dbContext.SaveChangesAsync(token);
    //         return true;
    //     }
    //     finally
    //     {
    //         _sem.Release();
    //     }
    // }

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

        var entity = await profile.ToHistoryEntity(userId, token);
        await _dbContext.HistoryRecords.AddAsync(entity, token);
        await _dbContext.SaveChangesAsync(token);
    }

    public async Task<string?> GetRecentTransferFile(string userId, string fileName, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        var existing = await _dbContext.HistoryRecords
            .Where(r => r.UserId == userId && Path.GetFileName(r.TransferDataFile) == fileName && File.Exists(r.TransferDataFile))
            .OrderByDescending(r => r.LastAccessed)
            .FirstOrDefaultAsync(token);

        return existing?.TransferDataFile;
    }

    private Task<HistoryRecordEntity?> Query(string userId, ProfileType type, string hash, CancellationToken token)
    {
        return _dbContext.HistoryRecords.FirstOrDefaultAsync(
            r => r.UserId == userId && r.Hash == hash && r.Type == type, token);
    }

    public async Task<string> GetProfileDataFolder(Profile profile, CancellationToken token = default)
    {
        var dirPath = Path.Combine(HistoryDataFolder, await profile.GetProfileId(token));
        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }
        return dirPath;
    }
}
