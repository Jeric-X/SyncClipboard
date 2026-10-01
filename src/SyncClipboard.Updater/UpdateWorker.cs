using System.ComponentModel;
using System.Diagnostics;
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
        MacDmgPackage? dmg = null;
        var canRestart = true;
        try
        {
            if (update.WorkDirectory is null)
                throw new IOException(UpdaterText.Current.WorkspaceNotPrepared);
            ValidateWorkspace(update.WorkDirectory);
            if (!await IsRunningInWorkspaceAsync(update.WorkDirectory, token))
                throw new IOException(UpdaterText.Current.OutsideWorkspace);
            if (OperatingSystem.IsMacOS())
                MacDmgPackage.ValidateTarget(update);
            else
                WindowsZipPackage.ValidateTarget(update);
            var writeDirectory = OperatingSystem.IsMacOS() ? Path.GetDirectoryName(update.Target)! : update.Target;
            var needsElevation = await RequiresElevationAsync(writeDirectory, update.Elevated, interaction, token);
            if (OperatingSystem.IsMacOS())
                macReplacement = new MacBundleReplacement(update.Target,
                    Path.Combine(update.WorkDirectory, "backup", "SyncClipboard.app"), needsElevation);
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
            await UpdateIo.RunAsync(UpdaterText.Current.LockInstallation + update.Target, () =>
            {
                targetLock = AcquireInstallationLock(update.Target);
                return Task.CompletedTask;
            }, interaction.AskFailureActionAsync, token);
            canRestart = true;

            dmg = await InstallAsync(update, interaction, macReplacement, token);
            targetLock?.Dispose();
            targetLock = null;
            await RestartAsync(update);
            return await CleanupAndReportAsync(update, interaction, macReplacement, dmg);
        }
        catch (Exception error)
        {
            if (dmg is not null)
                await DetachAfterFailureAsync(dmg, update, interaction);
            targetLock?.Dispose();
            targetLock = null;
            if (error is UpdateProcessExitException { Declined: true })
                return 3;
            Log(update, error.ToString());
            var recoveryRequired = error is UpdateRecoveryException or UpdateAbortedException || !canRestart;
            if (!recoveryRequired && error is not UpdateProcessExitException)
                await RestartIfStoppedAsync(update);
            var exitCode = error is UpdateProcessExitException ? 3 : recoveryRequired ? RecoveryRequired : 1;
            await interaction.ShowResultAsync(new UpdateResult(exitCode, error.Message, update.WorkDirectory, error switch
            {
                UpdateRecoveryException recovery => recovery.BackupPath,
                UpdateAbortedException aborted => aborted.BackupPath,
                _ => null
            }));
            return exitCode;
        }
        finally
        {
            targetLock?.Dispose();
        }
    }

    private static async Task<bool> IsRunningInWorkspaceAsync(string workspace, CancellationToken token)
    {
        var name = OperatingSystem.IsMacOS() ? "SyncClipboard.Updater" : "SyncClipboard.Updater.exe";
        var expected = Path.Combine(workspace, name);
        var executable = Path.GetFullPath(Environment.ProcessPath!);
        if (string.Equals(executable, expected, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!OperatingSystem.IsMacOS() || !File.Exists(expected))
            return false;
        // macOS reports /private/var for executables started through the system's /var temp path.
        var actualFile = await MacCommand.RunAsync("/usr/bin/stat", ["-f", "%d:%i", executable], token);
        var expectedFile = await MacCommand.RunAsync("/usr/bin/stat", ["-f", "%d:%i", expected], token);
        return actualFile == expectedFile;
    }

    internal static async Task<bool> RequiresElevationAsync(string target, bool elevated,
        IUpdateInteraction interaction, CancellationToken token)
    {
        var needsElevation = false;
        await UpdateIo.RunAsync(UpdaterText.Current.CheckWriteAccess + target, () =>
        {
            try
            {
                ProbeDirectoryWriteAccess(target);
            }
            catch (UnauthorizedAccessException) when (!elevated)
            {
                needsElevation = true;
            }
            return Task.CompletedTask;
        }, interaction.AskFailureActionAsync, token);
        return needsElevation;
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
        MacBundleReplacement? macReplacement, CancellationToken token)
    {
        string? stage = null;
        string? attempt = null;
        MacDmgPackage? dmg = null;
        try
        {
            interaction.Report("preparing", -1);
            await UpdateIo.RunAsync(UpdaterText.Current.PreparePackage + update.PackagePath + " -> " + update.WorkDirectory, async () =>
            {
                // Each retry gets a fresh destination, so partial extraction never conflicts with CreateNew.
                attempt = Path.Combine(update.WorkDirectory!, "attempt-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(attempt);
                if (OperatingSystem.IsMacOS())
                    dmg = await MacDmgPackage.PrepareAsync(update, attempt, token, interaction.AskFailureActionAsync);
                else
                    stage = await WindowsZipPackage.PrepareAsync(update, attempt, token);
            }, interaction.AskFailureActionAsync, token);
            interaction.Report("waiting", -1);
            await WaitForProcessAsync(update.ProcessId, update.ProcessStartTime, token, interaction.ConfirmForceExitAsync);
            if (macReplacement is not null)
                await macReplacement.ApplyAsync(dmg!.BundlePath, interaction, token);
            else
                await FileReplacement.ApplyAsync(stage!, update.Target, Path.Combine(attempt!, "backup"),
                    WindowsZipPackage.GetProtectedPaths(update), interaction.Report, token, interaction.AskFailureActionAsync);
            return dmg;
        }
        catch
        {
            if (dmg is not null)
                await DetachAfterFailureAsync(dmg, update, interaction);
            throw;
        }
    }

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
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
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
                while (true)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(gracefulWait ?? TimeSpan.FromSeconds(10));
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token);
                        return;
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                    if (process.HasExited)
                        return;
                    var action = await confirmForceExit(token);
                    if (action == ForceExitAction.No)
                        throw new UpdateProcessExitException(UpdaterText.Current.Canceled, declined: true);
                    token.ThrowIfCancellationRequested();
                    if (process.HasExited)
                        return;
                    if (action == ForceExitAction.Yes)
                        break;
                }
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
            catch (InvalidOperationException) when (process.HasExited) { }
        }
    }

    private static async Task RestartIfStoppedAsync(UpdateArguments update)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await WaitForProcessAsync(update.ProcessId, update.ProcessStartTime, timeout.Token);
            await RestartAsync(update);
        }
        catch (Exception error)
        {
            Log(update, UpdaterText.Current.RestartError + error);
        }
    }

    private static async Task RestartAsync(UpdateArguments update)
    {
        if (OperatingSystem.IsMacOS())
            await MacCommand.RunAsync("/usr/bin/open", ["-n", update.Target], CancellationToken.None);
        else if (OperatingSystem.IsWindows())
            WindowsProcessLauncher.Start(update.Executable, update.Target, update.AppElevated);
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
        MacBundleReplacement? macReplacement = null, MacDmgPackage? dmg = null)
    {
        UpdateResult result;
        try
        {
            Log(update, UpdaterText.Current.CleaningUp);
            if (dmg is not null)
                await dmg.DetachAsync(interaction.AskFailureActionAsync);
            if (macReplacement is not null)
                await macReplacement.CleanupAsync(interaction);
            await CleanupAsync(update.WorkDirectory!, interaction.AskFailureActionAsync);
            result = new UpdateResult(0);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            Log(update, error.ToString());
            var cause = error is UpdateAbortedException ? error.InnerException ?? error : error;
            result = new UpdateResult(1, cause.Message, update.WorkDirectory, macReplacement?.Backup, CleanupIncomplete: true);
        }
        await interaction.ShowResultAsync(result);
        return result.ExitCode;
    }

    internal static async Task CleanupAsync(string workspace, UpdateFailureHandler? onFailure = null)
    {
        ValidateWorkspace(workspace);
        var updaterPath = Path.Combine(workspace, "SyncClipboard.Updater.exe");
        var runningInWorkspace = string.Equals(Environment.ProcessPath, updaterPath, StringComparison.OrdinalIgnoreCase);
        await UpdateIo.RunAsync(UpdaterText.Current.RemoveWorkspace + workspace, () =>
        {
            if (!runningInWorkspace)
            {
                Directory.Delete(workspace, true);
                return Task.CompletedTask;
            }

            foreach (var path in Directory.EnumerateFileSystemEntries(workspace))
            {
                if (string.Equals(path, updaterPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Path.GetFileName(path) == WorkspaceMarker)
                    continue;

                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else
                    File.Delete(path);
            }
            return Task.CompletedTask;
        }, onFailure, CancellationToken.None);

        if (runningInWorkspace)
        {
            await UpdateIo.RunAsync(UpdaterText.Current.StartCleanup + workspace, () =>
            {
                using var cleanup = Process.Start(CreateSelfCleanupStartInfo(workspace))
                    ?? throw new IOException(UpdaterText.Current.CleanupStartFailed);
                return Task.CompletedTask;
            }, onFailure, CancellationToken.None);
        }
    }

    internal static ProcessStartInfo CreateSelfCleanupStartInfo(string workspace)
    {
        // Only the running executable and marker remain. Windows releases the EXE after we exit.
        // Pass paths through environment variables so they are never interpreted as command text.
        // Wait before the IF so every retry waits, including when deleting the EXE fails.
        const string command = """
            for /l %i in (1,1,60) do (
            "!SystemRoot!\System32\ping.exe" -n 2 127.0.0.1 >nul 2>&1
            & del /q "!SYNC_CLIPBOARD_CLEANUP_WORK!\SyncClipboard.Updater.exe" >nul 2>&1
            & if not exist "!SYNC_CLIPBOARD_CLEANUP_WORK!\SyncClipboard.Updater.exe" (
            del /q "!SYNC_CLIPBOARD_CLEANUP_WORK!\.syncclipboard-update" >nul 2>&1
            & rd "!SYNC_CLIPBOARD_CLEANUP_WORK!" >nul 2>&1
            & if not exist "!SYNC_CLIPBOARD_CLEANUP_WORK!\" exit /b 0
            )
            )
            """;
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
            Arguments = "/d /q /e:on /v:on /c " + command.ReplaceLineEndings(" ")
        };
        start.Environment["SYNC_CLIPBOARD_CLEANUP_WORK"] = workspace;
        return start;
    }

    internal static void ValidateWorkspace(string workspace)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(workspace));
        if (directory.Parent?.Name != "SyncClipboard-updates" || !Guid.TryParseExact(directory.Name, "N", out _)
            || !File.Exists(Path.Combine(directory.FullName, WorkspaceMarker)))
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
