using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.Interfaces;

public interface IInputPermissionProvider
{
    InputPermissionStatus GetStatus();
    InputPermissionStatus GetSimulationStatus();
    Task ResetAccessibilityPermissionAsync();
    void RequestAccessibilityPermission();
}
