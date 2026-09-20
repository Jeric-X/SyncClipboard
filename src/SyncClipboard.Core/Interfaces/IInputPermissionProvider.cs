using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.Interfaces;

public interface IInputPermissionProvider
{
    InputPermissionStatus GetStatus();
    void RequestAccessibilityPermission();
}
