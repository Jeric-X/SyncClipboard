using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
        Semaphore? targetLock = null;
        var canRestart = true;
        try
        {
            if (update.WorkDirectory is null)
                throw new IOException("The main application must prepare the updater workspace.");
            ValidateWorkspace(update.WorkDirectory);
            if (!string.Equals(Path.GetFullPath(Environment.ProcessPath!),
                Path.Combine(update.WorkDirectory, "SyncClipboard.Updater.exe"), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The update worker must run from its own workspace.");
            WindowsZipPackage.ValidateTarget(update);
            if (!update.Elevated)
            {
                canRestart = false;
                await UpdateIo.RunAsync("Lock installation / 锁定安装目录: " + update.Target, () =>
                {
                    targetLock = AcquireInstallationLock(update.Target);
                    return Task.CompletedTask;
                }, interaction.AskFailureActionAsync, token);
                canRestart = true;
            }

            if (await RequiresElevationAsync(update.Target, update.Elevated, interaction, token))
            {
                Log(update, "Requesting administrator permission for the installation directory.");
                var start = CreateStartInfo(Environment.ProcessPath!, update with { Elevated = true });
                start.UseShellExecute = true;
                start.Verb = "runas";
                using var elevated = Process.Start(start) ?? throw new IOException("Could not start the elevated updater.");
                // Wait for the worker to finish before considering a restart.
                await elevated.WaitForExitAsync(CancellationToken.None);
                if (elevated.ExitCode == 3)
                    return 3;
                if (elevated.ExitCode != 0 && elevated.ExitCode != 1)
                {
                    canRestart = false;
                    throw new IOException("The elevated update stopped without a complete rollback. Keep the backup for recovery.");
                }
                if (elevated.ExitCode != 0)
                    throw new IOException("The elevated update failed. See install.log for details.");
            }
            else
                await InstallAsync(update, interaction, token);
            if (update.Elevated)
                return 0;

            Restart(update);
            Log(update, "Update completed. Removing staging files and old backups.");
            await ScheduleCleanupAsync(update, interaction, token);
            await interaction.ShowResultAsync(new UpdateResult(0));
            return 0;
        }
        catch (Exception error)
        {
            targetLock?.Dispose();
            targetLock = null;
            if (error is UpdateProcessExitException { Declined: true })
                return 3;
            Log(update, error.ToString());
            var recoveryRequired = error is UpdateRecoveryException or UpdateAbortedException || !canRestart;
            if (!update.Elevated && !recoveryRequired && error is not UpdateProcessExitException)
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

    internal static async Task<bool> RequiresElevationAsync(string target, bool elevated,
        IUpdateInteraction interaction, CancellationToken token)
    {
        var needsElevation = false;
        await UpdateIo.RunAsync("Check directory write access / 检查目录写入权限: " + target, () =>
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

    private static async Task InstallAsync(UpdateArguments update, IUpdateInteraction interaction, CancellationToken token)
    {
        string? stage = null;
        string? attempt = null;
        interaction.Report("preparing", -1);
        await UpdateIo.RunAsync("Prepare package / 准备更新包: " + update.PackagePath + " -> " + update.WorkDirectory, async () =>
        {
            // Each retry gets a fresh destination, so partial extraction never conflicts with CreateNew.
            attempt = Path.Combine(update.WorkDirectory!, "attempt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(attempt);
            stage = await WindowsZipPackage.PrepareAsync(update, attempt, token);
        }, interaction.AskFailureActionAsync, token);
        interaction.Report("waiting", -1);
        await WaitForProcessAsync(update.ProcessId, update.ProcessStartTime, token, interaction.ConfirmForceExitAsync);
        await FileReplacement.ApplyAsync(stage!, update.Target, Path.Combine(attempt!, "backup"),
            WindowsZipPackage.GetProtectedPaths(update), interaction.Report, token, interaction.AskFailureActionAsync);
    }

    internal static Semaphore AcquireInstallationLock(string target)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        Semaphore semaphore;
        bool created;
        try
        {
            semaphore = new Semaphore(0, 1, @"Global\SyncClipboard.Update." + key, out created);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new IOException("Another user has locked this installation for update.", error);
        }
        // The object's existence is the lease; no thread-affine ownership or waiting is needed.
        // Closing the last handle, including on forced process termination, removes the lease.
        if (created)
            return semaphore;
        semaphore.Dispose();
        throw new IOException("Another updater is using this installation.");
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
                        throw new UpdateProcessExitException("Update canceled. / 已取消更新。", declined: true);
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
                        throw new UpdateProcessExitException("Could not force SyncClipboard to exit. / 无法强制退出主程序。", error);
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
            Restart(update);
        }
        catch (Exception error)
        {
            Log(update, "Could not restart the application: " + error);
        }
    }

    private static void Restart(UpdateArguments update)
    {
        using var process = Process.Start(new ProcessStartInfo(update.Executable)
        { UseShellExecute = true, WorkingDirectory = update.Target }) ?? throw new IOException("Could not restart SyncClipboard.");
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, UpdateArguments update)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in update.ToCommandLine())
            start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task ScheduleCleanupAsync(UpdateArguments update, IUpdateInteraction interaction, CancellationToken token)
    {
        await UpdateIo.RunAsync("Start cleanup / 启动清理: " + update.WorkDirectory, () =>
        {
            using var current = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(Path.Combine(update.Target, "SyncClipboard.Updater.exe"))
            { UseShellExecute = false, WorkingDirectory = update.Target };
            foreach (var argument in new[] { "--cleanup-work", update.WorkDirectory!, "--wait-pid",
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture), "--wait-start",
                current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), "--language", update.Language })
                start.ArgumentList.Add(argument);
            using var cleanup = Process.Start(start) ?? throw new IOException("Could not start updater cleanup.");
            return Task.CompletedTask;
        }, interaction.AskFailureActionAsync, token);
    }

    internal static async Task CleanupAsync(string workspace, int pid, long startTime, UpdateFailureHandler? onFailure = null)
    {
        ValidateWorkspace(workspace);
        await WaitForProcessAsync(pid, startTime, CancellationToken.None);
        ValidateWorkspace(workspace);
        await UpdateIo.RunAsync("Remove workspace / 删除工作目录: " + workspace, () =>
        {
            if (Directory.Exists(workspace))
                Directory.Delete(workspace, true);
            return Task.CompletedTask;
        }, onFailure, CancellationToken.None);
    }

    internal static void ValidateWorkspace(string workspace)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(workspace));
        if (directory.Parent?.Name != "SyncClipboard-updates" || !Guid.TryParseExact(directory.Name, "N", out _)
            || !File.Exists(Path.Combine(directory.FullName, WorkspaceMarker)))
            throw new IOException("Invalid updater workspace.");
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
