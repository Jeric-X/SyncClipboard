namespace SyncClipboard.Core.Models;

public sealed record HistoryQueryPara
{
    public ProfileTypeFilter TypeFilter { get; init; } = ProfileTypeFilter.All;
    public bool? Starred { get; init; }
    public string? SearchText { get; init; }
    public IReadOnlyCollection<string>? Tags { get; init; }
    public bool OnlyUntagged { get; init; }
}
