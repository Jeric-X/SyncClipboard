using SyncClipboard.Core.Utilities.Instances;
using System.Diagnostics;

namespace SyncClipboard.Core.Commons;

public static class StartArguments
{
    public const string ShutdownPrivious = "--shutdown-previous";
    public const string UpdateCompleted = "--update-completed";
    public const string CommandPrefix = "--command-";
    public const string ModifyStartupTask = "--modify-startup-task";
    public const string StartupTaskEnabledPrefix = "--startup-task-enabled=";
    public const string StartupTaskRunAsAdministratorPrefix = "--startup-task-run-as-administrator=";

    internal static void AddRestartArguments(ProcessStartInfo startInfo, AppDataDirectoryOptions options)
    {
        startInfo.ArgumentList.Add(ShutdownPrivious);
        if (options.AppDataDirectory is not null)
        {
            startInfo.ArgumentList.Add(AppDataDirectoryOptions.AppDataArgument);
            startInfo.ArgumentList.Add(options.AppDataDirectory);
        }
    }
}
