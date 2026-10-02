using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace SyncClipboard.Updater.Zip;

[SupportedOSPlatform("windows")]
internal static class WindowsProcessLauncher
{
    public static void Start(string executable, string workingDirectory, bool elevated, string? argument)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!elevated && new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            StartAsDesktopUser(executable, workingDirectory, argument);
            return;
        }

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = workingDirectory,
            Verb = elevated ? "runas" : string.Empty
        };
        if (argument is not null)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException(UpdaterText.Current.RestartFailed);
    }

    private static void StartAsDesktopUser(string executable, string workingDirectory, string? argument)
    {
        var shell = GetShellWindow();
        if (shell == 0 || GetWindowThreadProcessId(shell, out var processId) == 0)
            throw new IOException(UpdaterText.Current.DesktopUserUnavailable);

        const uint processQueryLimitedInformation = 0x1000;
        using var process = OpenProcess(processQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        const uint tokenQuery = 0x0008;
        const uint tokenDuplicate = 0x0002;
        const uint tokenAssignPrimary = 0x0001;
        if (!OpenProcessToken(process, tokenQuery | tokenDuplicate, out var shellToken))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using (shellToken)
        {
            using var identity = new WindowsIdentity(shellToken.DangerousGetHandle());
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new IOException(UpdaterText.Current.DesktopUserUnavailable);

            const int securityImpersonation = 2;
            const int tokenPrimary = 1;
            if (!DuplicateTokenEx(shellToken, tokenQuery | tokenDuplicate | tokenAssignPrimary,
                0, securityImpersonation, tokenPrimary, out var primaryToken))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using (primaryToken)
            {
                var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
                // A null environment uses the desktop user's profile, not the elevated updater's environment.
                var commandLine = Marshal.StringToHGlobalUni($"\"{executable}\" {argument}");
                try
                {
                    if (!CreateProcessWithTokenW(primaryToken, 0, executable, commandLine, 0, 0,
                        workingDirectory, ref startup, out var created))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    CloseHandle(created.Thread);
                    CloseHandle(created.Process);
                }
                finally
                {
                    Marshal.FreeHGlobal(commandLine);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort ReservedSize;
        public nint ReservedBytes;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle token, uint access, nint attributes,
        int impersonationLevel, int tokenType, out SafeAccessTokenHandle duplicate);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token, uint logonFlags, string application,
        nint commandLine, uint creationFlags, nint environment, string currentDirectory,
        ref StartupInfo startup, out ProcessInformation process);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
