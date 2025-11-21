namespace SyncClipboard.Shared.Profiles.Models;

public record ProfileLocalInfo
{
    public required string Text { get; init; }
    public string[] FilePaths { get; init; } = [];
}