using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Permissions;

namespace SyncClipboard.Core.Utilities.Permissions;

public sealed class WindowsPermissionProvider : IPermissionProvider
{
    public PermissionState GetState(PermissionCapability capability) => capability switch
    {
        PermissionCapability.SimulateInput or PermissionCapability.MonitorKeyboard or
            PermissionCapability.ReadWindow or PermissionCapability.ReadCaret => PermissionState.NotRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null)
    };

    public bool CheckAndRequest(PermissionCapability capability) => GetState(capability).IsGranted();

    public IReadOnlyList<PermissionItem> GetPermissions() => [];
}
