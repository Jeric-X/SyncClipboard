namespace SyncClipboard.Core.Models;

public enum NetworkAccountSwitchState
{
    Disabled,
    Evaluating,
    Matched,
    NoMatch,
    AccountRemoved,
    ManualOverride,
    Error,
}

public record NetworkAccountSwitchStatus
{
    public NetworkAccountSwitchState State { get; init; } = NetworkAccountSwitchState.Disabled;
    public string? RuleId { get; init; }
    public string? RuleName { get; init; }
    public string? AccountName { get; init; }
    public string? Error { get; init; }
    public string? Detail { get; init; }
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.Now;
}

