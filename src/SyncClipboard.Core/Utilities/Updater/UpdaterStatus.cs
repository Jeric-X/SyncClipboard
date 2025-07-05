namespace SyncClipboard.Core.Utilities.Updater;

public class UpdaterStatus(UpdaterState state, string? message, string? actionText, Func<CancellationToken, Task>? manualAction = null)
{
    public UpdaterState State { get; set; } = state;
    public string Message { get; set; } = message ?? string.Empty;
    public string ActionText { get; set; } = actionText ?? string.Empty;
    public Func<CancellationToken, Task>? ManualAction { get; } = manualAction ?? new((token) => Task.CompletedTask);
}