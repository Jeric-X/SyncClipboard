using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Utilities;

public class StartUpHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

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

    // Single-parameter overload for binary compatibility with existing compiled callers
    public static void Set(bool enable) => Set(enable, runAsAdmin: false);

    public static void Set(bool enable, bool runAsAdmin)
    {
        if (OperatingSystem.IsOSPlatform("windows"))
        {
            SetWindows(enable, runAsAdmin);
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
    private static void SetWindows(bool enable, bool runAsAdmin)
    {
        // Clean up old HKCU\Run entry on every transition (migration from <3.2.x)
        DeleteRegistryKey();

        if (enable)
        {
            CreateTaskViaCom(runAsAdmin);
        }
        else
        {
            DeleteTask();
        }
    }

    #region Task Scheduler (COM API)

    [SupportedOSPlatform("windows")]
    private static void CreateTaskViaCom(bool runAsAdmin)
    {
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service", true)!;
            dynamic scheduler = Activator.CreateInstance(schedulerType)!;
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder("\\");
            dynamic taskDef = scheduler.NewTask(0);

            taskDef.Principal.RunLevel = runAsAdmin ? 1 : 0; // 1=Highest, 0=LUA
            taskDef.Settings.DisallowStartIfOnBatteries = false;
            taskDef.Settings.StopIfGoingOnBatteries = false;
            taskDef.Settings.ExecutionTimeLimit = "PT0S"; // disable 72h default limit
            taskDef.Triggers.Create(9); // TASK_TRIGGER_LOGON

            dynamic action = taskDef.Actions.Create(0); // TASK_ACTION_EXEC
            action.Path = Env.ProgramPath;
            action.WorkingDirectory = Env.ProgramDirectory;

            // 6 = TASK_CREATE_OR_UPDATE, 3 = TASK_LOGON_INTERACTIVE_TOKEN
            folder.RegisterTaskDefinition(Env.SoftName, taskDef, 6, null, null, 3, null);
        }
        catch
        {
            try
            {
                var rl = runAsAdmin ? "/rl highest" : "";
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/create /tn \"{Env.SoftName}\" /tr \"\\\"{Env.ProgramPath}\\\"\" /sc onlogon {rl} /f",
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

    #endregion

    #region Registry (HKCU\Run) — migration cleanup

    [SupportedOSPlatform("windows")]
    private static void DeleteRegistryKey()
    {
        try
        {
            Registry.CurrentUser.OpenSubKey(RunKeyPath, true)?.DeleteValue(Env.SoftName, false);
        }
        catch
        {
            // Key may not exist
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckWindows()
    {
        // Primary: Task Scheduler
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

        // Fallback: legacy HKCU\Run entry (users upgrading from <3.2.x)
        var path = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            Env.SoftName, null);
        return path as string == Env.ProgramPath;
    }

    #endregion

    #region Linux

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

    #endregion
}
