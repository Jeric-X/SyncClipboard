namespace SyncClipboard.Core.Models.Keyboard;

public enum InputPermissionState
{
    Unknown,
    Available,
    Denied,
    Unavailable,
    Partial,
    NotRequired
}

public record InputPermissionStatus(
    InputPermissionState Accessibility,
    InputPermissionState KeyboardMonitoring,
    InputPermissionState InputSimulation)
{
    public bool CanSimulateInput => InputSimulation is InputPermissionState.Available or InputPermissionState.NotRequired;
}
