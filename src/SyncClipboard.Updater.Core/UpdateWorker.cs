using SyncClipboard.Updater.AppImage;
using SyncClipboard.Updater.Zip;
using SyncClipboard.Updater.Dmg;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SyncClipboard.Updater;

internal sealed class UpdateProcessExitException(string message, Exception? inner = null, bool declined = false) : IOException(message, inner)
{
    public bool Declined { get; } = declined;
}

internal static class UpdateWorker
{
    private const string WorkspaceMarker = ".syncclipboard-update";
    private const int RecoveryRequired = 2;

    public static async Task<int> RunAsync(UpdateArguments update, IUpdateInteraction interaction, CancellationToken token)
    {
        UpdaterText.Current = UpdaterText.ForLanguage(update.Language);
        IDisposable? targetLock = null;
        MacBundleReplacement? macReplacement = null;
        AppImageReplacement? appImageReplacement = null;
        MacDmgPackage? dmg = null;
        var canRestart = true;
        try
        {
            var workspace = await ValidateEnvironmentAsync(update, token);
            var needsElevation = await RequiresElevationAsync(GetWriteDirectories(update.Target), update.Elevated, interaction, token);
            if (OperatingSystem.IsMacOS())
                macReplacement = new MacBundleReplacement(update.Target,
                    Path.Combine(workspace, "backup", "SyncClipboard.app"), needsElevation);
            else if (OperatingSystem.IsLinux())
                appImageReplacement = new AppImageReplacement(update.Target,
                    Path.Combine(workspace, "backup", Path.GetFileName(update.Target)), needsElevation);
            else if (needsElevation)
            {
                Log(update, UpdaterText.Current.RequestingElevation);
                var start = CreateStartInfo(Environment.ProcessPath!, update with { Elevated = true });
                start.UseShellExecute = true;
                start.Verb = "runas";
                using var elevated = Process.Start(start) ?? throw new IOException(UpdaterText.Current.ElevatedStartFailed);
                return 0;
            }

            canRestart = false;
            await InteractiveOperation.RunAsync(UpdaterText.Current.LockInstallation + update.Target, () =>
            {
                targetLock = AcquireInstallationLock(update.Target);
                return Task.CompletedTask;
            }, interaction.AskFailureActionAsync, token);
            canRestart = true;

            dmg = await InstallAsync(update, interaction, macReplacement, appImageReplacement, token);
            targetLock?.Dispose();
            targetLock = null;
            await RestartAsync(update, updateCompleted: true);
            return await CleanupAndReportAsync(update, interaction, macReplacement, dmg, appImageReplacement);
        }
        catch (Exception error)
        {
            if (dmg is not null)
                await DetachAfterFailureAsync(dmg, update, interaction);
            targetLock?.Dispose();
            targetLock = null;
            return await ReportFailureAsync(update, interaction, error, canRestart, token);
        }
        finally
        {
            targetLock?.Dispose();
        }
    }

    private static async Task<string> ValidateEnvironmentAsync(UpdateArguments update, CancellationToken token)
    {
        if (update.WorkDirectory is null)
            throw new IOException(UpdaterText.Current.WorkspaceNotPrepared);
        ValidateWorkspace(update.WorkDirectory);
        if (!await IsRunningInWorkspaceAsync(update.WorkDirectory, token))
            throw new IOException(UpdaterText.Current.OutsideWorkspace);
        if (OperatingSystem.IsMacOS())
            MacDmgPackage.ValidateTarget(update);
        else if (OperatingSystem.IsLinux())
            AppImagePackage.ValidateTarget(update);
        else
            WindowsZipPackage.ValidateTarget(update);
        return update.WorkDirectory;
    }

    private static string[] GetWriteDirectories(string target)
    {
        if (OperatingSystem.IsMacOS())
            return [Path.GetDirectoryName(target)!, target];
        if (OperatingSystem.IsLinux())
            return [Path.GetDirectoryName(target)!];
        return [target];
    }

    private static async Task<int> ReportFailureAsync(UpdateArguments update, IUpdateInteraction interaction,
        Exception error, bool canRestart, CancellationToken token)
    {
        if (error is UpdateProcessExitException { Declined: true })
            return 3;
        Log(update, error.ToString());
        var recoveryRequired = error is UpdateRecoveryException or UpdateAbortedException || !canRestart;
        if (!recoveryRequired && error is not UpdateProcessExitException)
            await RestartIfStoppedAsync(update);
        var result = CreateFailureResult(update, error, recoveryRequired, token.IsCancellationRequested);
        await interaction.ShowResultAsync(result);
        return result.ExitCode;
    }

    private static UpdateResult CreateFailureResult(UpdateArguments update, Exception error, bool recoveryRequired, bool canceled)
    {
        var exitCode = error is UpdateProcessExitException ? 3 : recoveryRequired ? RecoveryRequired : 1;
        var message = error is OperationCanceledException && canceled
            ? UpdaterText.Current.Canceled : error.Message;
        return new UpdateResult(exitCode, message, update.WorkDirectory, error switch
        {
            UpdateRecoveryException recovery => recovery.BackupPath,
            UpdateAbortedException aborted => aborted.BackupPath,
            _ => null
        });
    }

    private static async Task<bool> IsRunningInWorkspaceAsync(string workspace, CancellationToken token)
    {
        var name = OperatingSystem.IsWindows() ? "SyncClipboard.Updater.exe" : "SyncClipboard.Updater";
        var expected = Path.Combine(workspace, name);
        var executable = Path.GetFullPath(Environment.ProcessPath!);
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (string.Equals(executable, expected, comparison))
            return true;
        if (!OperatingSystem.IsMacOS() || !File.Exists(expected))
            return false;
        // macOS reports /private/var for executables started through the system's /var temp path.
        var actualFile = await MacCommand.RunAsync("/usr/bin/stat", ["-f", "%d:%i", executable], token);
        var expectedFile = await MacCommand.RunAsync("/usr/bin/stat", ["-f", "%d:%i", expected], token);
        return actualFile == expectedFile;
    }

    internal static async Task<bool> RequiresElevationAsync(string[] directories, bool elevated,
        IUpdateInteraction interaction, CancellationToken token)
    {
        foreach (var directory in directories)
        {
            var needsElevation = false;
            await InteractiveOperation.RunAsync(UpdaterText.Current.CheckWriteAccess + directory, () =>
            {
                try
                {
                    ProbeDirectoryWriteAccess(directory);
                }
                catch (UnauthorizedAccessException) when (!elevated)
                {
                    needsElevation = true;
                }
                return Task.CompletedTask;
            }, interaction.AskFailureActionAsync, token);
            if (needsElevation)
                return true;
        }
        return false;
    }

    internal static void ProbeDirectoryWriteAccess(string target)
    {
        var probe = Path.Combine(target, ".syncclipboard-write-test-" + Guid.NewGuid().ToString("N"));
        using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1, FileOptions.DeleteOnClose);
        file.WriteByte(0);
        file.Flush();
    }

    private static async Task<MacDmgPackage?> InstallAsync(UpdateArguments update, IUpdateInteraction interaction,
        MacBundleReplacement? macReplacement, AppImageReplacement? appImageReplacement, CancellationToken token)
    {
        string? stage = null;
        string? attempt = null;
        MacDmgPackage? dmg = null;
        try
        {
            (attempt, stage, dmg) = await PreparePackageAsync(update, interaction, token);
            interaction.Report("waiting", -1);
            await WaitForProcessAsync(update.ProcessId, update.ProcessStartTime, token, interaction.ConfirmForceExitAsync);
            if (macReplacement is not null)
                await macReplacement.ApplyAsync(dmg!.BundlePath, interaction, token);
            else if (appImageReplacement is not null)
                await appImageReplacement.ApplyAsync(stage!, interaction, token);
            else
                await FileReplacement.ApplyAsync(stage!, update.Target, Path.Combine(attempt!, "backup"),
                    WindowsZipPackage.GetProtectedPaths(update), interaction.Report, token,
                    interaction.AskFailureActionAsync, interaction.SetRollbackAvailable);
            return dmg;
        }
        catch
        {
            if (dmg is not null)
                await DetachAfterFailureAsync(dmg, update, interaction);
            throw;
        }
    }

    internal static async Task<(string Attempt, string? Stage, MacDmgPackage? Dmg)> PreparePackageAsync(
        UpdateArguments update, IUpdateInteraction interaction, CancellationToken token)
    {
        string? attempt = null;
        string? stage = null;
        MacDmgPackage? dmg = null;
        interaction.Report("preparing", -1);
        await InteractiveOperation.RunAsync(UpdaterText.Current.PreparePackage + update.PackagePath + " -> " + update.WorkDirectory, async () =>
        {
            if (attempt is not null)
                await RemovePreparationAttemptAsync(attempt, interaction);
            attempt = Path.Combine(update.WorkDirectory!, "attempt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(attempt);
            if (OperatingSystem.IsMacOS())
                dmg = await MacDmgPackage.PrepareAsync(update, attempt, token, interaction.AskFailureActionAsync);
            else if (OperatingSystem.IsLinux())
                stage = await AppImagePackage.PrepareAsync(update, attempt, token);
            else
                stage = await WindowsZipPackage.PrepareAsync(update, attempt, token);
        }, interaction.AskFailureActionAsync, token);
        return (attempt!, stage, dmg);
    }

    private static Task RemovePreparationAttemptAsync(string attempt, IUpdateInteraction interaction)
        => InteractiveOperation.RunAsync(UpdaterText.Current.RemoveWorkspace + attempt, async () =>
        {
            if (OperatingSystem.IsMacOS())
                await MacDmgPackage.DetachAttemptAsync(attempt);
            Directory.Delete(attempt, true);
        }, interaction.AskFailureActionAsync, CancellationToken.None);

    private static async Task DetachAfterFailureAsync(MacDmgPackage dmg, UpdateArguments update, IUpdateInteraction interaction)
    {
        try
        {
            await dmg.DetachAsync(interaction.AskFailureActionAsync);
        }
        catch (Exception error)
        {
            // Keep the original installation failure and backup location available for recovery.
            Log(update, error.ToString());
        }
    }

    internal static IDisposable AcquireInstallationLock(string target)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (!OperatingSystem.IsLinux())
            normalized = normalized.ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        if (OperatingSystem.IsLinux())
        {
            // An abstract socket reserves the name across users without leaving an owned file behind.
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Bind(new UnixDomainSocketEndPoint("\0SyncClipboard.Update." + key));
                return socket;
            }
            catch (SocketException error)
            {
                socket.Dispose();
                var message = error.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? UpdaterText.Current.InstallationLocked : error.Message;
                throw new IOException(message, error);
            }
        }
        if (OperatingSystem.IsMacOS())
        {
            // Keep the file in place: unlinking a locked file would let a second updater lock a new inode.
            return new FileStream(Path.Combine("/tmp", "SyncClipboard.Update." + key + ".lock"),
                FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
        }
        Semaphore semaphore;
        bool created;
        try
        {
            semaphore = new Semaphore(0, 1, @"Global\SyncClipboard.Update." + key, out created);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new IOException(UpdaterText.Current.InstallationLockedByOtherUser, error);
        }
        // The object's existence is the lease; no thread-affine ownership or waiting is needed.
        // Closing the last handle, including on forced process termination, removes the lease.
        if (created)
            return semaphore;
        semaphore.Dispose();
        throw new IOException(UpdaterText.Current.InstallationLocked);
    }

    internal static async Task WaitForProcessAsync(int pid, long startTime, CancellationToken token,
        Func<CancellationToken, Task<ForceExitAction>>? confirmForceExit = null, TimeSpan? gracefulWait = null)
    {
        if (pid <= 0)
            return;
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return;
        }
        using (process)
        {
            try
            {
                if (process.HasExited)
                    return;
                if (startTime != 0 && process.StartTime.ToUniversalTime().Ticks != startTime)
                    return;
                if (confirmForceExit is null)
                {
                    await process.WaitForExitAsync(token);
                    return;
                }
                if (await ConfirmProcessExitAsync(process, confirmForceExit, gracefulWait, token))
                    await KillProcessAsync(process, token);
            }
            catch (InvalidOperationException) when (process.HasExited) { }
        }
    }

    private static async Task<bool> ConfirmProcessExitAsync(Process process,
        Func<CancellationToken, Task<ForceExitAction>> confirmForceExit, TimeSpan? gracefulWait, CancellationToken token)
    {
        while (true)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(gracefulWait ?? TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                return false;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            if (process.HasExited)
                return false;
            var action = await confirmForceExit(token);
            if (action == ForceExitAction.No)
                throw new UpdateProcessExitException(UpdaterText.Current.Canceled, declined: true);
            token.ThrowIfCancellationRequested();
            if (process.HasExited)
                return false;
            if (action == ForceExitAction.Yes)
                return true;
        }
    }

    private static async Task KillProcessAsync(Process process, CancellationToken token)
    {
        try
        {
            process.Kill();
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or TimeoutException)
        {
            if (!process.HasExited)
                throw new UpdateProcessExitException(UpdaterText.Current.ForceExitFailed, error);
        }
    }

    private static async Task RestartIfStoppedAsync(UpdateArguments update)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await WaitForProcessAsync(update.ProcessId, update.ProcessStartTime, timeout.Token);
            await RestartAsync(update, updateCompleted: false);
        }
        catch (Exception error)
        {
            Log(update, UpdaterText.Current.RestartError + error);
        }
    }

    internal static async Task RestartAsync(UpdateArguments update, bool updateCompleted)
    {
        var argument = updateCompleted ? "--update-completed" : null;
        if (OperatingSystem.IsMacOS())
        {
            List<string> arguments = ["-n", update.Target];
            if (argument is not null)
                arguments.AddRange(["--args", argument]);
            await MacCommand.RunAsync("/usr/bin/open", [.. arguments], CancellationToken.None);
        }
        else if (OperatingSystem.IsWindows())
            WindowsProcessLauncher.Start(WindowsZipPackage.GetExecutablePath(update.Target), update.Target, update.AppElevated, argument);
        else if (OperatingSystem.IsLinux())
        {
            var start = new ProcessStartInfo(update.Target)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(update.Target)!
            };
            if (argument is not null)
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException(UpdaterText.Current.RestartError + update.Target);
        }
        else
            throw new PlatformNotSupportedException(UpdaterText.Current.UnsupportedPlatform);
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, UpdateArguments update)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in update.ToCommandLine())
            start.ArgumentList.Add(argument);
        return start;
    }

    internal static async Task<int> CleanupAndReportAsync(UpdateArguments update, IUpdateInteraction interaction,
        MacBundleReplacement? macReplacement = null, MacDmgPackage? dmg = null,
        AppImageReplacement? appImageReplacement = null)
    {
        UpdateResult result;
        try
        {
            Log(update, UpdaterText.Current.CleaningUp);
            if (dmg is not null)
                await dmg.DetachAsync(interaction.AskFailureActionAsync);
            if (macReplacement is not null)
                await macReplacement.CleanupAsync(interaction);
            if (appImageReplacement is not null)
                await appImageReplacement.CleanupAsync(interaction);
            await CleanupAsync(update.WorkDirectory!, interaction.AskFailureActionAsync);
            result = new UpdateResult(0);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            Log(update, error.ToString());
            result = CreateCleanupFailureResult(update, error, macReplacement, appImageReplacement);
        }
        await interaction.ShowResultAsync(result);
        return result.ExitCode;
    }

    private static UpdateResult CreateCleanupFailureResult(UpdateArguments update, Exception error,
        MacBundleReplacement? macReplacement, AppImageReplacement? appImageReplacement)
    {
        var cause = error is UpdateAbortedException ? error.InnerException ?? error : error;
        string? backup = null;
        if (Directory.Exists(macReplacement?.Backup))
            backup = macReplacement.Backup;
        else if (File.Exists(appImageReplacement?.Backup))
            backup = appImageReplacement.Backup;
        return new UpdateResult(1, cause.Message, update.WorkDirectory, backup, CleanupIncomplete: true);
    }

    internal static async Task CleanupAsync(string workspace, UpdateFailureHandler? onFailure = null)
    {
        ValidateWorkspace(workspace);
        var updaterPath = Path.Combine(workspace, "SyncClipboard.Updater.exe");
        var runningInWorkspace = OperatingSystem.IsWindows()
            && string.Equals(Environment.ProcessPath, updaterPath, StringComparison.OrdinalIgnoreCase);
        await InteractiveOperation.RunAsync(UpdaterText.Current.RemoveWorkspace + workspace, () =>
        {
            if (runningInWorkspace)
            {
                // WinUI keeps its DLLs and resources mapped until the updater has exited.
                using var cleanup = Process.Start(CreateSelfCleanupStartInfo(workspace))
                    ?? throw new IOException(UpdaterText.Current.CleanupStartFailed);
            }
            else
                Directory.Delete(workspace, true);
            return Task.CompletedTask;
        }, onFailure, CancellationToken.None);
    }

    internal static ProcessStartInfo CreateSelfCleanupStartInfo(string workspace)
        => WindowsWorkspaceCleanup.CreateStartInfo(workspace);

    internal static void ValidateWorkspace(string workspace)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(workspace));
        var namedWorkspace = directory.Name.StartsWith("SyncClipboard-update-", StringComparison.Ordinal)
            && directory.Name.Length > "SyncClipboard-update-".Length;
        var groupedWorkspace = directory.Parent?.Name == "SyncClipboard-updates"
            && Guid.TryParseExact(directory.Name, "N", out _);
        if ((!namedWorkspace && !groupedWorkspace) || !File.Exists(Path.Combine(directory.FullName, WorkspaceMarker)))
            throw new IOException(UpdaterText.Current.InvalidWorkspace);
    }

    private static void Log(UpdateArguments update, string message)
    {
        if (update.WorkDirectory is null)
            return;
        try
        {
            File.AppendAllText(Path.Combine(update.WorkDirectory, "install.log"), $"{DateTime.UtcNow:O} {message}\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(error);
        }
    }
}
