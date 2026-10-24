namespace SyncClipboard.Core.Models.Permissions;

public enum PermissionState
{
    Unknown,
    Available,
    Denied,
    Unavailable,
    Partial,
    NotRequired
}

public static class PermissionStateExtensions
{
    public static bool IsGranted(this PermissionState state) =>
        state is PermissionState.Available or PermissionState.NotRequired;
}
