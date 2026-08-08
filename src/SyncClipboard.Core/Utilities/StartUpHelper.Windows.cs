using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;

namespace SyncClipboard.Core.Utilities;

public partial class StartUpHelper
{
    [SupportedOSPlatform("windows")]
    private static string StartupTaskName
    {
        get
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("Unable to determine the current Windows user.");
            return $"{Env.SoftName} Startup-{sid}";
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindows(bool enable, bool runAsAdministrator)
    {
        if (enable)
        {
            CreateScheduledTask(runAsAdministrator);
            DeleteRegistryEntry();
        }
        else
        {
            DeleteScheduledTask();
            DeleteRegistryEntry();
        }
    }

    public static async Task SetAsync(bool enable, bool runAsAdministrator = false)
    {
        if (OperatingSystem.IsWindows() && !Env.IsRunningAsAdministrator)
        {
            if (runAsAdministrator || GetWindowsTaskRunAsAdministrator() == true)
            {
                await UpdateWindowsStartupTaskWithElevationAsync(enable, runAsAdministrator);
                return;
            }

            try
            {
                Set(enable, runAsAdministrator);
            }
            catch (Exception ex) when (IsAccessDenied(ex))
            {
                // Retry the task operation through the elevated helper.
                await UpdateWindowsStartupTaskWithElevationAsync(enable, runAsAdministrator);
            }

            return;
        }

        Set(enable, runAsAdministrator);
    }

    private static bool IsAccessDenied(Exception exception)
    {
        const int AccessDeniedHResult = unchecked((int)0x80070005);
        return exception.HResult == AccessDeniedHResult ||
            exception is UnauthorizedAccessException ||
            (exception.InnerException is not null && IsAccessDenied(exception.InnerException));
    }

    [SupportedOSPlatform("windows")]
    private static void EnsureCurrentUserCanRequestElevation()
    {
        if (!Env.IsUserInAdministratorGroup)
        {
            throw new InvalidOperationException(Strings.AdministratorPermissionRequired);
        }
    }

    public static bool TryUpdateWindowsStartupTaskFromArguments(string[] args, out int returnCode)
    {
        if (Array.IndexOf(args, StartArguments.ModifyStartupTask) < 0)
        {
            returnCode = default;
            return false;
        }

        try
        {
            var enable = GetStartupTaskArgument(args, StartArguments.StartupTaskEnabledPrefix);
            var runAsAdministrator = GetStartupTaskArgument(args, StartArguments.StartupTaskRunAsAdministratorPrefix);
            Set(enable, runAsAdministrator);
            returnCode = (int)ReturnCode.Success;
        }
        catch
        {
            returnCode = (int)ReturnCode.StartupTaskModificationFailed;
        }

        return true;
    }

    private static bool GetStartupTaskArgument(string[] args, string prefix)
    {
        var argument = args.FirstOrDefault(item => item.StartsWith(prefix, StringComparison.Ordinal));
        if (argument is null || !bool.TryParse(argument[prefix.Length..], out var value))
        {
            throw new ArgumentException($"Missing or invalid startup task argument: {prefix}");
        }

        return value;
    }

    [SupportedOSPlatform("windows")]
    private static async Task UpdateWindowsStartupTaskWithElevationAsync(bool enable, bool runAsAdministrator)
    {
        EnsureCurrentUserCanRequestElevation();
        var arguments = $"{StartArguments.ModifyStartupTask} " +
            $"{StartArguments.StartupTaskEnabledPrefix}{enable} " +
            $"{StartArguments.StartupTaskRunAsAdministratorPrefix}{runAsAdministrator}";

        var startInfo = new ProcessStartInfo
        {
            FileName = Env.ProgramPath,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(Strings.AdminStartupTaskElevationFailed);
            await process.WaitForExitAsync();
            if (process.ExitCode == (int)ReturnCode.StartupTaskModificationFailed)
            {
                throw new InvalidOperationException(Strings.StartupTaskModificationFailed);
            }

            if (process.ExitCode != (int)ReturnCode.Success)
            {
                throw new InvalidOperationException(Strings.AdminStartupTaskElevationFailed);
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException(Strings.AdminStartupTaskElevationCancelled, ex);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(Strings.AdminStartupTaskElevationFailed, ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CreateScheduledTask(bool runAsAdministrator)
    {
        dynamic taskService = CreateTaskService();
        dynamic taskDefinition = taskService.NewTask(0);
        taskDefinition.RegistrationInfo.Author = Env.SoftName;
        taskDefinition.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN
        taskDefinition.Principal.RunLevel = runAsAdministrator ? 1 : 0; // TASK_RUNLEVEL_HIGHEST / TASK_RUNLEVEL_LUA
        taskDefinition.Settings.ExecutionTimeLimit = "PT0S";
        taskDefinition.Settings.DisallowStartIfOnBatteries = false;
        taskDefinition.Settings.StopIfGoingOnBatteries = false;

        using var identity = WindowsIdentity.GetCurrent();
        dynamic trigger = taskDefinition.Triggers.Create(9); // TASK_TRIGGER_LOGON
        trigger.UserId = identity.Name;
        dynamic action = taskDefinition.Actions.Create(0); // TASK_ACTION_EXEC
        action.Path = Env.ProgramPath;

        dynamic taskFolder = taskService.GetFolder("\\");
        taskFolder.RegisterTaskDefinition(
            StartupTaskName,
            taskDefinition,
            6, // TASK_CREATE_OR_UPDATE
            null,
            null,
            3, // TASK_LOGON_INTERACTIVE_TOKEN
            null);
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteScheduledTask()
    {
        if (CheckScheduledTask())
        {
            dynamic taskService = CreateTaskService();
            dynamic taskFolder = taskService.GetFolder("\\");
            taskFolder.DeleteTask(StartupTaskName, 0);
        }
    }

    public static bool? GetWindowsTaskRunAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var task = GetTaskDefinition();
            if (task is null || !IsTaskForCurrentProgram(task))
            {
                return null;
            }

            return task.Descendants().Any(element =>
                element.Name.LocalName == "RunLevel" &&
                string.Equals(element.Value.Trim(), "HighestAvailable", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static dynamic CreateTaskService()
    {
        var taskServiceType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        dynamic taskService = Activator.CreateInstance(taskServiceType)
            ?? throw new InvalidOperationException("Unable to access Windows Task Scheduler.");
        taskService.Connect();
        return taskService;
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteRegistryEntry()
    {
        Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)?.DeleteValue(Env.SoftName, false);
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckScheduledTask()
    {
        try
        {
            var task = GetTaskDefinition();
            return task is not null && IsTaskForCurrentProgram(task);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static XDocument? GetTaskDefinition()
    {
        var startInfo = CreateSchtasksStartInfo("/Query");
        startInfo.ArgumentList.Add("/TN");
        startInfo.ArgumentList.Add(StartupTaskName);
        startInfo.ArgumentList.Add("/XML");

        var (ExitCode, StandardOutput) = RunSchtasks(startInfo, ignoreFailure: true);
        return ExitCode == 0 ? XDocument.Parse(StandardOutput) : null;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsTaskForCurrentProgram(XDocument task)
    {
        return task
            .Descendants()
            .Where(element => element.Name.LocalName == "Exec")
            .SelectMany(element => element.Elements().Where(child => child.Name.LocalName == "Command"))
            .Any(element => Env.IsSamePath(element.Value.Trim().Trim('"'), Env.ProgramPath));
    }

    [SupportedOSPlatform("windows")]
    private static ProcessStartInfo CreateSchtasksStartInfo(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    [SupportedOSPlatform("windows")]
    private static (int ExitCode, string StandardOutput) RunSchtasks(ProcessStartInfo startInfo, bool ignoreFailure = false)
    {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start Windows Task Scheduler.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(standardOutputTask, standardErrorTask);
        var standardOutput = standardOutputTask.Result;
        var standardError = standardErrorTask.Result;

        if (!ignoreFailure && process.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
            throw new InvalidOperationException($"{Strings.StartupTaskModificationFailed} {error.Trim()}");
        }

        return (process.ExitCode, standardOutput);
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckWindows()
    {
        if (CheckScheduledTask())
        {
            return true;
        }

        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Env.SoftName, null);
        return path is string value && Env.IsSamePath(value.Trim().Trim('"'), Env.ProgramPath);
    }
}
