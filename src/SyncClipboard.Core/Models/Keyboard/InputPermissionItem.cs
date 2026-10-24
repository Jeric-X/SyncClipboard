namespace SyncClipboard.Core.Models.Keyboard;

public record InputPermissionItem(
    string Name,
    string Description,
    InputPermissionState State,
    IReadOnlyList<InputPermissionAction> Actions);

public record InputPermissionAction(string Text, Func<Task> ExecuteAsync);
