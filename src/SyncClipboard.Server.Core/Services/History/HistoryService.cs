using Microsoft.EntityFrameworkCore;
using System.Threading;
using SyncClipboard.Abstract;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Utilities.History;

namespace SyncClipboard.Server.Core.Services.History;

public class HistoryService : IHistoryService
{
    private readonly HistoryDbContext _dbContext;
    private readonly IWebHostEnvironment _env;
    // When using SQLite provider we need a process-wide semaphore to avoid concurrent write issues.
    private static readonly SemaphoreSlim _sqliteSem = new(1, 1);
    // Per-instance semaphore used when not using SQLite
    private readonly SemaphoreSlim _sem;

    public HistoryService(HistoryDbContext dbContext, IWebHostEnvironment env)
    {
        _dbContext = dbContext;
        _env = env;
        // Database migrations will be applied at application startup.
        // Choose semaphore strategy based on EF Core provider
        // Use shared static semaphore for SQLite to serialize DB file access across instances
        // Otherwise use an instance semaphore
        _sem = _dbContext.Database.IsSqlite() ? _sqliteSem : new SemaphoreSlim(1, 1);
    }

    public async Task<HistoryRecordDto?> GetAsync(string userId, string hash, ProfileType type, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        try
        {
            var entity = await _dbContext.HistoryRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Hash == hash && r.Type == type, token);
            if (entity == null) return null;

            entity.LastAccessed = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(token);
            return HistoryRecordDto.FromEntity(entity);
        }
        finally
        {
            _sem.Release();
        }
    }

    public async Task<List<HistoryRecordDto>> GetListAsync(string userId, ProfileType type, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        try
        {
            var list = await _dbContext.HistoryRecords
                .Where(r => r.UserId == userId && (type == ProfileType.None || r.Type == type))
                .OrderByDescending(r => r.Timestamp)
                .ToListAsync(token);

            return list.Select(HistoryRecordDto.FromEntity).ToList();
        }
        finally
        {
            _sem.Release();
        }
    }

    public async Task SetAsync(string userId, HistoryRecordEntity record, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        try
        {
            var existing = await _dbContext.HistoryRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Hash == record.Hash && r.Type == record.Type, token);
            if (existing != null)
            {
                // Update fields
                existing.Text = record.Text;
                existing.FilePathJson = record.FilePathJson;
                existing.Timestamp = record.Timestamp;
                existing.Stared = record.Stared;
                existing.Pinned = record.Pinned;
                existing.Size = record.Size;
                existing.Mime = record.Mime;
                existing.LastAccessed = DateTime.UtcNow;
            }
            else
            {
                record.UserId = userId;
                record.LastAccessed = DateTime.UtcNow;
                await _dbContext.HistoryRecords.AddAsync(record, token);
            }

            await _dbContext.SaveChangesAsync(token);
        }
        finally
        {
            _sem.Release();
        }
    }

    public async Task SetWithDataAsync(string userId, string hash, ProfileType type, Stream? data, string? fileName = null, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        try
        {
            var existing = await _dbContext.HistoryRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Hash == hash && r.Type == type, token);

            if (existing != null)
            {
                // existing record: only update LastAccessed and return. Do not accept/overwrite data in this flow.
                existing.LastAccessed = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(token);
                return;
            }
            else
            {
                var record = new HistoryRecordEntity
                {
                    UserId = userId,
                    Hash = hash,
                    Type = type,
                    Text = string.Empty,
                    Stared = false,
                    Pinned = false,
                    // Mime left null unless set by other flows
                    Timestamp = DateTime.UtcNow,
                    LastAccessed = DateTime.UtcNow
                };

                await _dbContext.HistoryRecords.AddAsync(record, token);
                existing = record;
            }

            // Handle data stream: store under WebRootPath/data/files/{userId}/{hash}/{fileName}
            if (data != null)
            {
                var webRoot = string.IsNullOrEmpty(_env.WebRootPath) ? _env.ContentRootPath : _env.WebRootPath;
                var baseFolder = Path.Combine(webRoot, "data", "files", userId, hash);
                if (!Directory.Exists(baseFolder)) Directory.CreateDirectory(baseFolder);

                var safeFileName = string.IsNullOrEmpty(fileName) ? "data.bin" : fileName;
                var filePath = Path.Combine(baseFolder, safeFileName);

                // Write stream to file
                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await data.CopyToAsync(fs, token);
                }

                // update metadata
                existing.FilePath = [filePath];
                existing.Size = new FileInfo(filePath).Length;
            }

            await _dbContext.SaveChangesAsync(token);
        }
        finally
        {
            _sem.Release();
        }
    }

    public async Task<bool> UpdateAsync(string userId, string hash, ProfileType type, HistoryRecordUpdateDto dto, CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        try
        {
            var existing = await _dbContext.HistoryRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Hash == hash && r.Type == type, token);
            if (existing == null) return false;

            if (dto.Text is not null) existing.Text = dto.Text;
            if (dto.Stared.HasValue) existing.Stared = dto.Stared.Value;
            if (dto.Pinned.HasValue) existing.Pinned = dto.Pinned.Value;

            existing.LastAccessed = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(token);
            return true;
        }
        finally
        {
            _sem.Release();
        }
    }
}
