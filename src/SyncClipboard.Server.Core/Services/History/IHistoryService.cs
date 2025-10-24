// using SyncClipboard.Server.Core.Models;

// namespace SyncClipboard.Server.Core.Services.History;

// public interface IHistoryService
// {
//     Task<HistoryRecordDto?> GetAsync(string userId, string hash, ProfileType type, CancellationToken token = default);
//     Task<List<HistoryRecordDto>> GetListAsync(string userId, ProfileType type, CancellationToken token = default);
//     Task SetAsync(string userId, HistoryRecordEntity record, CancellationToken token = default);
//     // Set metadata and optionally store binary data. 'data' can be null.
//     Task SetWithDataAsync(string userId, string hash, ProfileType type, Stream? data, string? fileName = null, CancellationToken token = default);
//     // Update metadata of an existing history record using DTO (partial updates allowed)
//     Task<bool> UpdateAsync(string userId, string hash, ProfileType type, HistoryRecordUpdateDto dto, CancellationToken token = default);
// }
