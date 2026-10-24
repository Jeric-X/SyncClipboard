namespace SyncClipboard.Core.Models.Permissions;

/// <summary>A business capability whose platform-specific permission requirements must be checked.</summary>
public enum PermissionCapability
{
    SimulateInput,
    MonitorKeyboard,
    ReadWindow,
    ReadCaret
}
