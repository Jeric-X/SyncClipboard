using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.Utilities.Keyboard;

public sealed class WindowsInputPermissionProvider : IInputPermissionProvider
{
    public InputPermissionStatus GetStatus() => new(
        InputPermissionState.NotRequired, InputPermissionState.NotRequired, InputPermissionState.NotRequired);

    public InputPermissionStatus GetSimulationStatus() => GetStatus();

    public IReadOnlyList<InputPermissionItem> GetPermissions() => [];

    public bool CheckAndRequestAccessibilityPermission() => true;
}
