namespace SyncClipboard.Updater.Core;

public enum UpdateOutcome { Succeeded, Failed, Canceled }

public sealed record UpdateResult(string? TargetVersion, UpdateOutcome Outcome, string? Error = null)
{
    public int ProtocolVersion { get; init; } = 1;
    public DateTimeOffset FinishedAt { get; init; } = DateTimeOffset.UtcNow;
}
