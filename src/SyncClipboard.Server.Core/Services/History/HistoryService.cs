using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Shared.Models;
using SyncClipboard.Server.Core.Utilities.History;
using SyncClipboard.Shared.Utilities;
using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Hubs;
using SyncClipboard.Server.Core.Exceptions;
using System.Linq.Expressions;
using System.Text.Json;

namespace SyncClipboard.Server.Core.Services.History;

public class HistoryService : IHistoryEntityRepository<HistoryRecordEntity, DateTime>
{
    private string? _cleanupUserId;
    private readonly ServerEnvProvider? _serverEnv;

    private readonly IOptions<AppSettings> _options;
    private readonly HistoryDbContext _dbContext;
    private readonly string _persistentDir;
    private readonly IHubContext<SyncClipboardHub, ISyncClipboardClient> _hubContext;
    private static readonly SemaphoreSlim _processSem = new(1, 1);
    private readonly SemaphoreSlim _sem;

    public HistoryService(HistoryDbContext dbContext,
        IProfileEnv profileEnv,
        IHubContext<SyncClipboardHub, ISyncClipboardClient> hubContext,
        IOptions<AppSettings>? options = null)
    {
        _options = options ?? Options.Create(new AppSettings());
        _dbContext = dbContext;
        _serverEnv = profileEnv as ServerEnvProvider;
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

        if (dto.Tags is not null)
            dto.Tags = HistoryTags.Normalize(dto.Tags);
        dto.Title = HistoryTitles.Normalize(dto.Title);

        var updated = ApplyMetadataUpdate(existing, dto);
        if (updated is null)
            return (null, null);

        if (updated.Value || BackfillMissingMetadata(existing, dto.Tags, dto.Title))
        {
            await _dbContext.SaveChangesAsync(token);
            await NotifyProfileChangeAsync(existing);
            if (updated.Value)
                await DeleteProfileDataIfNeed(existing, token);
        }

        return (updated, HistoryRecordDto.FromEntity(existing));
    }

    private static bool? ApplyMetadataUpdate(HistoryRecordEntity existing, HistoryRecordUpdateDto dto)
    {
        dto.Version ??= existing.Version + 1;
        dto.LastModified ??= DateTimeOffset.UtcNow;
        if (!HistoryHelper.ShouldUpdate(existing.Version, dto.Version.Value,
            new DateTimeOffset(existing.LastModified), dto.LastModified.Value))
            return false;

        if (dto.IsDelete is false && existing.IsDeleted && !string.IsNullOrEmpty(existing.TransferDataFile))
            return null;

        existing.LastModified = ResolveModificationTime(existing, dto.IsDelete, dto.LastModified.Value.UtcDateTime);
        ApplyOptionalUpdateFields(existing, dto);
        existing.Version = dto.Version.Value;
        return true;
    }

    private static DateTime ResolveModificationTime(HistoryRecordEntity existing, bool? incomingDeleted, DateTime incomingTime)
    {
        var latest = existing.LastModified > incomingTime ? existing.LastModified : incomingTime;
        if (!existing.IsDeleted || incomingDeleted != false)
            return latest;

        var now = DateTime.UtcNow;
        return now > latest ? now : latest;
    }

    private static void ApplyOptionalUpdateFields(HistoryRecordEntity existing, HistoryRecordUpdateDto dto)
    {
        if (dto.Tags is not null)
            existing.Tags = dto.Tags;
        existing.Title = dto.Title ?? existing.Title;
        if (dto.Starred.HasValue)
            existing.Stared = dto.Starred.Value;
        if (dto.Pinned.HasValue)
            existing.Pinned = dto.Pinned.Value;
        if (dto.IsDelete.HasValue)
            existing.IsDeleted = dto.IsDelete.Value;

        if (dto.LastAccessed.HasValue)
            existing.LastAccessed = dto.LastAccessed.Value.UtcDateTime;
    }

    private async Task NotifyProfileChangeAsync(HistoryRecordEntity entity)
    {
        if (_hubContext is null)
            return;

        try
        {
            var historyRecordDto = HistoryRecordDto.FromEntity(entity);
            await _hubContext.Clients.User(entity.UserId).RemoteHistoryChanged(historyRecordDto);
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
        bool onlyUntagged = false,
        IReadOnlyCollection<string>? tags = null,
        bool searchTitles = false,
        CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var query = _dbContext.HistoryRecords
            .Where(r => r.UserId == userId);

        query = ApplyTagFilter(query, tags, onlyUntagged);

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
            var includedTypes = Enum.GetValues<ProfileType>()
                .Where(t => (types & (ProfileTypeFilter)(1 << (int)t)) != 0)
                .ToList();

            if (includedTypes.Count == 0)
            {
                return [];
            }

            query = query.Where(r => includedTypes.Contains(r.Type));
        }

        query = HistorySearch.Apply(query, searchText, searchTitles);

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

    private static IQueryable<HistoryRecordEntity> ApplyTagFilter(
        IQueryable<HistoryRecordEntity> query, IReadOnlyCollection<string>? tags, bool onlyUntagged)
    {
        var names = HistoryTags.NormalizeNames(tags ?? []);
        if (names.Length > 0 && onlyUntagged)
            throw new ArgumentException("Tags and untagged filter cannot be combined.");
        if (names.Length > 0)
            query = query.Where(r => r.Tags != null && r.Tags.Any(name => names.Contains(name)));
        if (onlyUntagged)
            query = query.Where(r => r.Tags == null || r.Tags.Length == 0);

        return query;
    }

    public async Task AddProfile(string userId, Profile profile, CancellationToken token)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());
        await StoreProfile(userId, profile, token);
    }

    private async Task StoreProfile(string userId, Profile profile, CancellationToken token)
    {
        var entity = await profile.ToHistoryEntity(_persistentDir, userId, token);
        var type = profile.Type;
        var hash = await profile.GetHash(token);

        var existing = await Query(userId, type, hash, token);
        if (existing is not null)
        {
            existing.LastAccessed = DateTime.UtcNow;
            existing.IsDeleted = false;
            existing.LastModified = DateTime.UtcNow;
            existing.TransferDataFile = entity.TransferDataFile;
            existing.TransferDataHash = entity.TransferDataHash;
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
            .Where(r => r.UserId == userId && !r.IsDeleted)
            .OrderByDescending(r => r.LastAccessed)
            .AsEnumerable()
            .Where(r => Path.GetFileName(r.TransferDataFile) == fileName && File.Exists(Profile.GetFullPath(_persistentDir, r.Type, r.Hash, r.TransferDataFile)))
            .Select(r => Profile.GetFullPath(_persistentDir, r.Type, r.Hash, r.TransferDataFile))
            .FirstOrDefault();

        return existing;
    }

    public async Task<FileHashInfo?> GetTransferDataByProfileId(
        string userId, string profileId, CancellationToken token = default)
    {
        if (!Profile.ParseProfileId(profileId, out var type, out var hash) || string.IsNullOrEmpty(hash))
        {
            return null;
        }

        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var entity = await Query(userId, type, hash, token);
        if (entity is null || entity.IsDeleted)
        {
            return null;
        }

        try
        {
            return await PrepareTransferData(entity, token);
        }
        catch (LocalProfileDataUnavailableException ex)
        {
            throw new HistoryTransferDataException("Stored transfer data is invalid and cannot be regenerated.", ex);
        }
    }

    private async Task<FileHashInfo?> PrepareTransferData(HistoryRecordEntity entity, CancellationToken token)
    {
        var profile = entity.ToProfile(_persistentDir);
        var transferData = await profile.PrepareTransferData(_persistentDir, token);
        if (transferData is null)
        {
            return null;
        }

        var persistentInfo = await profile.Persist(_persistentDir, token);
        UpdateEntityLocalData(entity, persistentInfo, persistentInfo.TransferDataHash);
        await _dbContext.SaveChangesAsync(token);
        return transferData;
    }

    private Task<HistoryRecordEntity?> Query(string userId, ProfileType type, string hash, CancellationToken token)
    {
        return _dbContext.HistoryRecords.FirstOrDefaultAsync(
            r => r.UserId == userId && EF.Functions.Collate(r.Hash, "NOCASE") == hash && r.Type == type, token);
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

    public async Task<ProfileDto> SaveCurrentClipboard(string userId, ProfileDto dto, string clipboardDir, CancellationToken token)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());
        var existing = string.IsNullOrEmpty(dto.Hash) ? null : await Query(userId, dto.Type, dto.Hash, token);
        Profile profile;
        string? uploadPath = null;
        if (existing is { IsDeleted: false })
        {
            profile = existing.ToProfile(_persistentDir);
        }
        else
        {
            profile = Profile.Create(dto with { Hash = dto.Hash?.ToUpperInvariant() ?? "", TransferDataHash = null });
            if (dto.HasData)
            {
                uploadPath = Path.Combine(clipboardDir, "file", dto.DataName!);
                if (!File.Exists(uploadPath))
                    throw new FileNotFoundException();
                await using var upload = File.OpenRead(uploadPath);
                profile = await StoreVerifiedAttachment(profile, dto.DataName!, upload, dto.TransferDataHash, token);
            }
            else if (!await profile.IsLocalDataValid(false, token))
            {
                throw new InvalidDataException("Inline data does not match the profile hash.");
            }
        }

        await StoreProfile(userId, profile, token);
        var saved = await profile.ToProfileDto(token);
        saved.SyncedAt = DateTimeOffset.Now;
        Directory.CreateDirectory(clipboardDir);
        var target = Path.Combine(clipboardDir, "SyncClipboard.json");
        var temp = target + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(saved), token);
            File.Move(temp, target, true);
        }
        finally
        {
            File.Delete(temp);
        }
        if (uploadPath is not null)
            File.Delete(uploadPath);
        return saved;
    }

    public async Task CommitClipboardUpload(string temp, string target, CancellationToken token)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());
        File.Move(temp, target, true);
    }

    public async Task<HistoryRecordDto> AddRecordDto(
        string userId, HistoryRecordDto incoming, string? declaredTransferDataHash,
        Stream? transferFileStream, CancellationToken token = default)
    {
        if (incoming.Tags is not null)
            incoming.Tags = HistoryTags.Normalize(incoming.Tags);
        incoming.Title = HistoryTitles.Normalize(incoming.Title);

        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var existing = await Query(userId, incoming.Type, incoming.Hash, token);
        if (existing is not null)
        {
            return await UpdateExistingRecordDto(
                userId, incoming, existing, declaredTransferDataHash, transferFileStream, token);
        }

        return await AddNewRecordDto(userId, incoming, declaredTransferDataHash, transferFileStream, token);
    }

    private async Task<HistoryRecordDto> UpdateExistingRecordDto(
        string userId, HistoryRecordDto incoming, HistoryRecordEntity existing,
        string? declaredTransferDataHash, Stream? transferFileStream, CancellationToken token)
    {
        var restoring = existing.IsDeleted && !incoming.IsDeleted;
        if (restoring)
        {
            await EnsureExistingRecordData(existing, declaredTransferDataHash, transferFileStream, token);
        }

        var shouldUpdate = restoring || HistoryHelper.ShouldUpdate(existing.Version, incoming.Version,
            new DateTimeOffset(existing.LastModified), incoming.LastModified);
        if (shouldUpdate)
        {
            var updatedEntity = incoming.ToEntity(userId);
            updatedEntity.Version = Math.Max(incoming.Version, existing.Version + 1);
            updatedEntity.LastModified = ResolveModificationTime(existing, incoming.IsDeleted, updatedEntity.LastModified);
            UpdateEntityFields(updatedEntity, existing);
            existing.Title = incoming.Title ?? existing.Title;
            if (incoming.Tags is not null)
                existing.Tags = [.. incoming.Tags];
        }
        var backfilled = !shouldUpdate && BackfillMissingMetadata(existing, incoming.Tags, incoming.Title);
        if (shouldUpdate || backfilled)
        {
            await _dbContext.SaveChangesAsync(token);
            await NotifyProfileChangeAsync(existing);
            if (shouldUpdate)
                await DeleteProfileDataIfNeed(existing, token);
        }

        return HistoryRecordDto.FromEntity(existing);
    }

    private static bool BackfillMissingMetadata(HistoryRecordEntity existing, string[]? tags, string? title)
    {
        var backfillTags = existing.Tags is null && tags is { Length: > 0 };
        var backfillTitle = existing.Title is null && title is { Length: > 0 };
        if (backfillTags)
            existing.Tags = tags;
        if (backfillTitle)
            existing.Title = title;
        return backfillTags || backfillTitle;
    }

    private async Task EnsureExistingRecordData(
        HistoryRecordEntity existing, string? incomingTransferDataHash,
        Stream? transferFileStream, CancellationToken token)
    {
        if (transferFileStream is not null)
        {
            await SaveTransferDataAsync(existing, incomingTransferDataHash, transferFileStream, token);
            return;
        }

        var existingProfile = existing.ToProfile(_persistentDir);
        if (await existingProfile.IsLocalDataValid(true, token) is false)
        {
            throw new ArgumentException("Needs tranfer data.");
        }
    }

    private async Task<HistoryRecordDto> AddNewRecordDto(
        string userId, HistoryRecordDto incoming, string? declaredTransferDataHash,
        Stream? transferFileStream, CancellationToken token)
    {
        var entity = incoming.ToEntity(userId);

        if (transferFileStream != null)
        {
            await SaveTransferDataAsync(entity, declaredTransferDataHash, transferFileStream, token);
        }
        else
        {
            entity.TransferDataHash = null;
            var profile = entity.ToProfile(_persistentDir);
            if (!await profile.IsLocalDataValid(false, token))
            {
                throw new ArgumentException("Local data is missing or does not match the profile hash.");
            }
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

    private static void UpdateEntityLocalData(
        HistoryRecordEntity entity, ProfilePersistentInfo persistentInfo, string? transferDataHash)
    {
        entity.TransferDataFile = persistentInfo.TransferDataFile ?? string.Empty;
        entity.TransferDataHash = transferDataHash;
        entity.FilePaths = persistentInfo.FilePaths;
        entity.Text = persistentInfo.Text;
        entity.Size = persistentInfo.Size;
    }

    private async Task SaveTransferDataAsync(
        HistoryRecordEntity entity, string? declaredTransferDataHash,
        Stream transferFileStream, CancellationToken token)
    {
        var profile = entity.ToProfile(_persistentDir);
        var proposedPath = profile.GetTransferDataSavePath(_persistentDir)
            ?? throw new HistoryTransferDataException("Profile does not support transfer data.");
        try
        {
            profile = await StoreVerifiedAttachment(profile, Path.GetFileName(proposedPath), transferFileStream, declaredTransferDataHash, token);
            var info = await profile.Persist(_persistentDir, token);
            UpdateEntityLocalData(entity, info, info.TransferDataHash);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            throw new HistoryTransferDataException("History transfer data failed validation.", ex);
        }
    }

    private async Task<Profile> StoreVerifiedAttachment(Profile profile, string fileName, Stream upload, string? declaredHash, CancellationToken token)
    {
        // Validate in a private directory before touching any file used by another account.
        var stage = Path.Combine(_persistentDir, ".upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            if (string.IsNullOrEmpty(fileName) || fileName is "." or ".." || Path.GetFileName(fileName) != fileName)
                throw new InvalidDataException("Invalid attachment name.");
            if (profile.Type is ProfileType.Group or ProfileType.Text)
            {
                var expectedHash = await profile.GetHash(token);
                if (string.IsNullOrEmpty(expectedHash) || File.Exists(Profile.GetFullPath(_persistentDir, profile.Type, expectedHash, fileName)))
                    fileName = Guid.NewGuid().ToString("N") + Path.GetExtension(fileName);
            }
            var path = Path.Combine(stage, fileName);
            await using (var output = File.Create(path))
                await upload.CopyToAsync(output, token);
            var sha256 = await Utility.VerifyFileSHA256(path, declaredHash, token);
            var stagedRoot = Path.Combine(stage, Path.GetRandomFileName());
            await profile.SetAndMoveTransferData(stagedRoot, new FileHashInfo(path, sha256), token);
            var hash = await profile.GetHash(token);
            var info = await profile.Persist(stagedRoot, token);
            var shared = await _dbContext.HistoryRecords.AsNoTracking()
                .Where(r => r.Type == profile.Type && r.Hash == hash && !r.IsDeleted && r.TransferDataFile != "")
                .OrderByDescending(r => r.TransferDataHash == sha256)
                .FirstOrDefaultAsync(token);
            bool reuseFiles = shared is not null && await shared.ToProfile(_persistentDir).IsLocalDataValid(false, token);
            if (reuseFiles)
            {
                var archive = Profile.GetFullPath(_persistentDir, shared!.Type, shared.Hash, shared.TransferDataFile);
                if (File.Exists(archive) && await Utility.FileMatchesSHA256(archive, sha256, token))
                    return Profile.Create(_persistentDir, info with { FilePaths = shared.FilePaths, TransferDataFile = shared.TransferDataFile });

                // Equivalent groups may have different ZIP metadata. Keep the verified wire bytes
                // for each representation while sharing the extracted content.
                if (profile.Type == ProfileType.Group)
                    info = info with { FilePaths = shared.FilePaths };
            }
            var source = Profile.QueryGetWorkingDir(stagedRoot, profile.Type, hash);
            var dest = Profile.CreateWorkingDir(_persistentDir, profile.Type, hash);
            if (!reuseFiles || profile.Type != ProfileType.Group)
            {
                foreach (var dir in Directory.EnumerateDirectories(source))
                    Directory.Move(dir, Path.Combine(dest, Path.GetFileName(dir)));
            }
            foreach (var file in Directory.EnumerateFiles(source))
                File.Move(file, Path.Combine(dest, Path.GetFileName(file)), true);
            return Profile.Create(_persistentDir, info);
        }
        finally
        {
            Directory.Delete(stage, true);
        }
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
        if (Directory.Exists(workingDir) && !await HasActiveReference(entity.Type, entity.Hash, token)
            && _serverEnv?.CurrentAttachmentDirectories().Contains(Path.GetFileName(workingDir)) != true)
        {
            try
            {
                Directory.Delete(workingDir, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private Task<bool> HasActiveReference(ProfileType type, string hash, CancellationToken token) =>
        _dbContext.HistoryRecords.AnyAsync(r => r.Type == type && EF.Functions.Collate(r.Hash, "NOCASE") == hash && !r.IsDeleted, token);

    public async Task RemoveOutOfDateDeletedRecords(CancellationToken token = default)
    {
        await _sem.WaitAsync(token);
        using var guard = new ScopeGuard(() => _sem.Release());

        var toDeletes = await _dbContext.HistoryRecords
            .Where(r => r.IsDeleted && r.LastModified < DateTime.UtcNow.AddDays(-30)).ToListAsync(token);

        if (toDeletes.Count == 0)
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

    public Task RemoveOutOfRetentionRecords(CancellationToken token = default)
    {
        return _historyManagerHelper.RemoveExpiredInBatchesAsync(token);
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

    public Expression<Func<HistoryRecordEntity, bool>> QueryCount => entity => (_cleanupUserId == null || entity.UserId == _cleanupUserId) && !entity.IsDeleted;
    public Expression<Func<HistoryRecordEntity, bool>> AutoDeletePredicate =>
        entity => (_cleanupUserId == null || entity.UserId == _cleanupUserId) && !entity.Stared && !entity.Pinned && !entity.IsDeleted
                  && (!_options.Value.ProtectTaggedRecords || entity.Tags == null || entity.Tags.Length == 0)
                  && (!_options.Value.ProtectTitledRecords || string.IsNullOrEmpty(entity.Title));
    public Expression<Func<HistoryRecordEntity, DateTime>> QueryLastActivityTime => entity =>
        entity.LastModified > entity.LastAccessed ? entity.LastModified : entity.LastAccessed;

    public uint HistoryRetentionMinutes => _options.Value.HistoryRetentionMinutes;

    public Expression<Func<HistoryRecordEntity, DateTime>> QueryDeleteOrderBy => entity => entity.LastModified > entity.LastAccessed ? entity.LastModified : entity.LastAccessed;

    public async Task MarkForDeletionAsync(HistoryRecordEntity entity, CancellationToken token)
    {
        if (_dbContext.Entry(entity).State == EntityState.Detached)
        {
            // 外部单独调用：Query 拿到被跟踪的 existing，改 existing 字段（会被 SaveChanges 持久化）
            var existing = await Query(entity.UserId, entity.Type, entity.Hash, token);
            if (existing is null)
            {
                return;
            }
            existing.IsDeleted = true;
            existing.LastModified = DateTime.UtcNow;
            existing.Version++;
            // 同步到原 entity 用于后续 OnRecordDeletedAsync 的广播/删文件
            entity.IsDeleted = existing.IsDeleted;
            entity.LastModified = existing.LastModified;
            entity.Version = existing.Version;
            return;
        }

        // helper 路径：entity 已被 ToListAsync 跟踪，直接改字段
        entity.IsDeleted = true;
        entity.LastModified = DateTime.UtcNow;
        entity.Version++;
    }

    public Task<int> SaveChangesAsync(CancellationToken token)
    {
        return _dbContext.SaveChangesAsync(token);
    }

    public async Task OnRecordDeletedAsync(HistoryRecordEntity entity, CancellationToken token)
    {
        await NotifyProfileChangeAsync(entity);
        await DeleteProfileDataIfNeed(entity, token);
    }

    public async Task OnBatchStartAsync(CancellationToken token)
    {
        await _sem.WaitAsync(token);
    }

    public async Task OnBatchEndAsync(CancellationToken token)
    {
        _dbContext.ChangeTracker.Clear();
        _sem.Release();
        await Task.Delay(TimeSpan.FromSeconds(10), token);
    }

    public async Task<uint> SetRecordsMaxCount(uint maxCount, CancellationToken token = default)
    {
        if (maxCount == 0)
            return 0;
        var users = await RecordDbSet.Where(r => !r.IsDeleted).GroupBy(r => r.UserId)
            .Where(group => group.Count() > maxCount).Select(group => group.Key).ToListAsync(token);
        uint count = 0;
        try
        {
            foreach (var user in users)
            {
                _cleanupUserId = user;
                count += await _historyManagerHelper.SetRecordsMaxCount(maxCount, token);
            }
        }
        finally
        {
            _cleanupUserId = null;
        }
        return count;
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

        // Count only the attachment paths referenced by this account.
        long totalFileSizeBytes = 0;
        var paths = records.Where(r => !r.IsDeleted)
            .SelectMany(r => r.FilePaths.Append(r.TransferDataFile).Where(p => !string.IsNullOrEmpty(p))
                .Select(p => Profile.GetFullPath(_persistentDir, r.Type, r.Hash, p)))
            .Distinct();
        foreach (var path in paths)
        {
            if (File.Exists(path))
                totalFileSizeBytes += new FileInfo(path).Length;
            else if (Directory.Exists(path))
                totalFileSizeBytes += new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
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

        var current = _serverEnv?.CurrentAttachmentDirectories();
        var directories = Directory.GetDirectories(_persistentDir);
        foreach (var directory in directories)
        {
            token.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(directory);
            if (dirName.StartsWith(".upload-", StringComparison.Ordinal) && Guid.TryParseExact(dirName[8..], "N", out _))
            {
                Directory.Delete(directory, true);
                continue;
            }
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
            if (current?.Contains(dirName) != true && !await HasActiveReference(type, hash, token))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
