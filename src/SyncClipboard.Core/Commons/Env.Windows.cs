using System.Runtime.Versioning;
using System.Security.Principal;

namespace SyncClipboard.Core.Commons;

public static partial class Env
{
    [SupportedOSPlatform("windows")]
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
