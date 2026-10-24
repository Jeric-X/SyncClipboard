using SyncClipboard.Core.Models.Permissions;

namespace SyncClipboard.Core.Interfaces;

public interface IPermissionProvider
{
    /// <summary>Checks only the requested capability without prompting or requesting access.</summary>
    PermissionState GetState(PermissionCapability capability);

    /// <summary>Returns true if already authorized; otherwise offers authorization and returns false.</summary>
    bool CheckAndRequest(PermissionCapability capability);

    IReadOnlyList<PermissionItem> GetPermissions();
}
