namespace SyncClipboard.Core.Models.Permissions;

public record PermissionItem(
    string Name,
    string Description,
    PermissionState State,
    IReadOnlyList<PermissionAction> Actions);

public record PermissionAction(string Text, Func<Task> ExecuteAsync);
