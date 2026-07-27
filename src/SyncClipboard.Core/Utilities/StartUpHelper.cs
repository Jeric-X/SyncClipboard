using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Utilities;

public class StartUpHelper
{
    public static bool Status()
    {
        if (OperatingSystem.IsOSPlatform("windows"))
        {
            return CheckWindows();
        }
        else if (OperatingSystem.IsOSPlatform("linux"))
        {
            return CheckLinux();
        }
        else
        {
            throw new PlatformNotSupportedException("This method is only supported on Windows and Linux.");
        }
    }

    public static void Set(bool enable)
    {
        if (OperatingSystem.IsOSPlatform("windows"))
        {
            SetWindows(enable);
        }
        else if (OperatingSystem.IsOSPlatform("linux"))
        {
            SetLinux(enable);
        }
        else
        {
            throw new PlatformNotSupportedException("This method is only supported on Windows and Linux.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindows(bool enable)
    {
        if (enable)
        {
            if (IsElevated())
            {
                // Running elevated: must use Task Scheduler — HKCU\Run is blocked by UAC
                CreateTaskViaCom();
                CleanupRegistryKey();
            }
            else
            {
                // Running normally: use registry — schtasks /rl highest would fail without elevation
                SetRegistry(true);
                DeleteTask();
            }
        }
        else
        {
            SetRegistry(false);
            DeleteTask();
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CreateTaskViaCom()
    {
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service", true)!;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder("\\");
            dynamic taskDef = scheduler.NewTask(0);

            taskDef.Principal.RunLevel = 1; // TASK_RUNLEVEL_HIGHEST
            taskDef.Settings.DisallowStartIfOnBatteries = false;
            taskDef.Settings.StopIfGoingOnBatteries = false;
            taskDef.Triggers.Create(9); // TASK_TRIGGER_LOGON

            dynamic action = taskDef.Actions.Create(0); // TASK_ACTION_EXEC
            action.Path = Env.ProgramPath;
            action.WorkingDirectory = Env.ProgramDirectory;

            // 6 = TASK_CREATE_OR_UPDATE, 3 = TASK_LOGON_INTERACTIVE_TOKEN
            folder.RegisterTaskDefinition(Env.SoftName, taskDef, 6, null, null, 3, null);
        }
        catch
        {
            // Fallback: schtasks CLI (no WorkingDirectory — may fail for WinUI3/self-contained apps)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/create /tn \"{Env.SoftName}\" /tr \"\\\"{Env.ProgramPath}\\\"\" /sc onlogon /rl highest /f",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch
            {
                // Both methods failed — give up silently
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteTask()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/delete /tn \"{Env.SoftName}\" /f",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi)?.WaitForExit();
        }
        catch
        {
            // Task may not exist
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetRegistry(bool enable)
    {
        if (enable)
        {
            Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
                Env.SoftName, Env.ProgramPath);
        }
        else
        {
            Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true)
                ?.DeleteValue(Env.SoftName, false);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CleanupRegistryKey()
    {
        try
        {
            Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true)
                ?.DeleteValue(Env.SoftName, false);
        }
        catch
        {
            // Key may not exist
        }
    }

    [SupportedOSPlatform("linux")]
    private static void SetLinux(bool enable)
    {
        var autoStartFolder = Path.Combine(Env.UserAppDataDirectory, "autostart");
        if (enable)
        {
            if (Directory.Exists(autoStartFolder) is false)
            {
                Directory.CreateDirectory(autoStartFolder);
            }

            DesktopEntryHelper.SetLinuxDesktopEntry(autoStartFolder);
        }
        else
        {
            DesktopEntryHelper.RemvoeLinuxDesktopEntry(autoStartFolder);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckWindows()
    {
        // Check task scheduler first
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = $"/query /tn \"{Env.SoftName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var process = Process.Start(psi);
        process?.WaitForExit();
        if (process?.ExitCode == 0)
        {
            return true;
        }

        // Fallback: check old registry method
        var path = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            Env.SoftName, null);
        return path as string == Env.ProgramPath;
    }

    [SupportedOSPlatform("linux")]
    private static bool CheckLinux()
    {
        var desktopFileName = $"{Env.LinuxPackageAppId}.desktop";
        var autoStartFolder = Path.Combine(Env.UserAppDataDirectory, "autostart");
        var autoStartDestkopFilePath = Path.Combine(autoStartFolder, desktopFileName);
        var fileInfo = new FileInfo(autoStartDestkopFilePath);
        if (fileInfo.Exists is false)
        {
            return false;
        }

        if (fileInfo.Length > 1024 * 1024)  // 1Mb
        {
            return false;
        }

        return File.ReadLines(autoStartDestkopFilePath).Any(line => line == $"TryExec={Env.ProgramPath}");
    }
}
