using Microsoft.Win32.SafeHandles;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SyncClipboard.Core.Commons;

public static partial class Env
{
    [SupportedOSPlatform("windows")]
    public static bool IsRunningAsAdministrator { get; } =
        OperatingSystem.IsWindows() && CheckIsRunningAsAdministrator();

    [SupportedOSPlatform("windows")]
    public static bool IsUserInAdministratorGroup { get; } =
        OperatingSystem.IsWindows() && CheckIsUserInAdministratorGroup();

    [SupportedOSPlatform("windows")]
    private static bool CheckIsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckIsUserInAdministratorGroup()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            return true;
        }

        if (GetTokenElevationType(identity.AccessToken) != TokenElevationType.Limited ||
            !GetTokenInformation(
                identity.AccessToken.DangerousGetHandle(),
                TokenInformationClass.LinkedToken,
                out TokenLinkedToken linkedToken,
                Marshal.SizeOf<TokenLinkedToken>(),
                out _))
        {
            return false;
        }

        using var linkedTokenHandle = new SafeAccessTokenHandle(linkedToken.Handle);
        return IsTokenInAdministratorGroup(linkedTokenHandle.DangerousGetHandle());
    }

    [SupportedOSPlatform("windows")]
    private static bool IsTokenInAdministratorGroup(IntPtr tokenHandle)
    {
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var sidBytes = new byte[administratorsSid.BinaryLength];
        administratorsSid.GetBinaryForm(sidBytes, 0);
        var pinnedSid = GCHandle.Alloc(sidBytes, GCHandleType.Pinned);
        try
        {
            return CheckTokenMembership(tokenHandle, pinnedSid.AddrOfPinnedObject(), out var isMember) && isMember;
        }
        finally
        {
            pinnedSid.Free();
        }
    }

    private static TokenElevationType GetTokenElevationType(SafeAccessTokenHandle token)
    {
        return GetTokenInformation(
            token.DangerousGetHandle(),
            TokenInformationClass.ElevationType,
            out TokenElevationType elevationType,
            sizeof(int),
            out _)
            ? elevationType
            : TokenElevationType.Default;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        out TokenElevationType tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        out TokenLinkedToken tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CheckTokenMembership(
        IntPtr tokenHandle,
        IntPtr sidToCheck,
        [MarshalAs(UnmanagedType.Bool)] out bool isMember);

    private enum TokenInformationClass
    {
        ElevationType = 18,
        LinkedToken = 19
    }

    private enum TokenElevationType
    {
        Default = 1,
        Full = 2,
        Limited = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenLinkedToken
    {
        public IntPtr Handle;
    }
}
