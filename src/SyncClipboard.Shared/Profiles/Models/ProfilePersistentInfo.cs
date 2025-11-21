namespace SyncClipboard.Shared.Profiles.Models;

public record ProfilePersistentInfo
{
    public required ProfileType Type { get; init; }
    public required string Text { get; init; }
    public required long Size { get; init; }
    public required string Hash { get; init; }
    public string? TransferDataFile { get; init; }
    public string[] FilePaths { get; init; } = [];
}