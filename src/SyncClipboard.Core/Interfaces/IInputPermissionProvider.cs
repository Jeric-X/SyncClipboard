using SyncClipboard.Core.Models.Keyboard;
using System.ComponentModel;

namespace SyncClipboard.Core.Interfaces;

public interface IInputPermissionProvider : INotifyPropertyChanged
{
    bool HasRequestedAccessibilityPermission { get; }
    InputPermissionStatus GetStatus();
    InputPermissionStatus GetSimulationStatus();
    bool CheckAndRequestAccessibilityPermission();
}
