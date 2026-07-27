using System.Diagnostics;
using System.Runtime.Versioning;
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

    public static void Set(bool enable, bool runAsAdmin = false)
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
        if (enable)
        {
            CreateTaskViaCom(runAsAdmin);
        }
        else
        {
            DeleteTask();
        }
    }

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

            taskDef.Principal.RunLevel = runAsAdmin ? 1 : 0; // 1 = TASK_RUNLEVEL_HIGHEST, 0 = TASK_RUNLEVEL_LUA
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
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = $"/query /tn \"{Env.SoftName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var process = Process.Start(psi);
        process?.WaitForExit();
        return process?.ExitCode == 0;
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
