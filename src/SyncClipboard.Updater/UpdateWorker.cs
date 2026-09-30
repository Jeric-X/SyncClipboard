using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SyncClipboard.Updater;

internal sealed class UpdateProcessExitException(string message, Exception? inner = null) : IOException(message, inner);

internal static class UpdateWorker
{
    private const string WorkspaceMarker = ".syncclipboard-update";
    private const int RecoveryRequired = 2;

    public static async Task<int> RunAsync(UpdateArguments update, IUpdateInteraction interaction, CancellationToken token)
    {
        if (update.WorkDirectory is null)
            return await RelocateAsync(update, interaction, token);
        ValidateWorkspace(update.WorkDirectory);
        if (!string.Equals(Path.GetFullPath(Environment.ProcessPath!),
            Path.Combine(update.WorkDirectory, "SyncClipboard.Updater.exe"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The update worker must run from its own workspace.");

        FileStream? targetLock = null;
        var canRestart = true;
        try
        {
            WindowsZipPackage.ValidateTarget(update);
            if (!update.Elevated)
            {
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(update.Target.ToUpperInvariant())));
                var path = Path.Combine(Path.GetDirectoryName(update.WorkDirectory)!, key + ".lock");
                try
                {
                    targetLock = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                        1, FileOptions.DeleteOnClose);
                }
                catch (IOException error) when (IsSharingViolation(error))
                {
                    canRestart = false;
                    throw new IOException("Another updater is using this installation.");
                }
            }

            try
            {
                await InstallAsync(update, interaction, token);
            }
            catch (UnauthorizedAccessException) when (!update.Elevated && !token.IsCancellationRequested)
            {
                Log(update, "Retrying with administrator permission after restoring the original files.");
                var start = CreateStartInfo(Environment.ProcessPath!, update with { Elevated = true });
                start.UseShellExecute = true;
                start.Verb = "runas";
                using var elevated = Process.Start(start) ?? throw new IOException("Could not start the elevated updater.");
                // Wait for the worker to finish before considering a restart.
                await elevated.WaitForExitAsync(CancellationToken.None);
                if (elevated.ExitCode != 0 && elevated.ExitCode != 1)
                {
                    canRestart = false;
                    throw new IOException("The elevated update stopped without a complete rollback. Keep the backup for recovery.");
                }
                if (elevated.ExitCode != 0)
                    throw new IOException("The elevated update failed. See install.log for details.");
            }
            if (update.Elevated)
                return 0;

            Restart(update);
            Log(update, "Update completed. Removing staging files and old backups.");
            ScheduleCleanup(update);
            await interaction.ShowResultAsync(new UpdateResult(0));
            return 0;
        }
        catch (Exception error)
        {
            targetLock?.Dispose();
            targetLock = null;
            Log(update, error.ToString());
            var recoveryRequired = error is UpdateRecoveryException or UpdateAbortedException || !canRestart;
            if (!update.Elevated && !recoveryRequired && error is not UpdateProcessExitException)
                await RestartIfStoppedAsync(update);
            var exitCode = recoveryRequired ? RecoveryRequired : 1;
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

    private static async Task<int> RelocateAsync(UpdateArguments update, IUpdateInteraction interaction, CancellationToken token)
    {
        string? workspace = null;
        try
        {
            WindowsZipPackage.ValidateTarget(update);
            var root = Path.Combine(Path.GetTempPath(), "SyncClipboard-updates");
            Directory.CreateDirectory(root);
            workspace = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            File.WriteAllText(Path.Combine(workspace, WorkspaceMarker), "SyncClipboard updater workspace v1");
            var worker = Path.Combine(workspace, "SyncClipboard.Updater.exe");
            await WindowsZipPackage.CopyAsync(Environment.ProcessPath!, worker, token);
            token.ThrowIfCancellationRequested();
            using var launcher = Process.GetCurrentProcess();
            using var process = Process.Start(CreateStartInfo(worker, update with
            {
                WorkDirectory = workspace,
                LauncherId = launcher.Id,
                LauncherStartTime = launcher.StartTime.ToUniversalTime().Ticks
            }))
                ?? throw new IOException("Could not start the relocated updater.");
            return 0;
        }
        catch (Exception error)
        {
            if (workspace is not null)
                Log(update with { WorkDirectory = workspace }, error.ToString());
            await RestartIfStoppedAsync(update);
            await interaction.ShowResultAsync(new UpdateResult(1, error.Message, workspace));
            return 1;
        }
    }

    private static async Task InstallAsync(UpdateArguments update, IUpdateInteraction interaction, CancellationToken token)
    {
        var attempt = Path.Combine(update.WorkDirectory!, "attempt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(attempt);
        interaction.Report("preparing", -1);
        var stage = await WindowsZipPackage.PrepareAsync(update, attempt, token);
        interaction.Report("waiting", -1);
        await WaitForProcessAsync(update.ProcessId, update.ProcessStartTime, token,
            interaction.ConfirmForceExitAsync);
        await WaitForProcessAsync(update.LauncherId, update.LauncherStartTime, token);
        await FileReplacement.ApplyAsync(stage, update.Target, Path.Combine(attempt, "backup"),
            WindowsZipPackage.GetProtectedPaths(update), interaction.Report, token,
            (path, error, cancellation) =>
            {
                Log(update, $"File operation failed: {path}: {error}");
                // Preserve the automatic elevation attempt after restoring the original files.
                if (error is UnauthorizedAccessException && !update.Elevated)
                    return Task.FromException<UpdateFailureAction>(error);
                return interaction.AskFailureActionAsync(path, error, cancellation);
            });
    }

    internal static bool IsSharingViolation(IOException error) => (error.HResult & 0xFFFF) is 32 or 33;

    internal static async Task WaitForProcessAsync(int pid, long startTime, CancellationToken token,
        Func<CancellationToken, Task<bool>>? confirmForceExit = null, TimeSpan? gracefulWait = null)
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
                if (!await confirmForceExit(token))
                    throw new UpdateProcessExitException("Update canceled. / 已取消更新。");
                token.ThrowIfCancellationRequested();
                if (process.HasExited)
                    return;
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

    private static void ScheduleCleanup(UpdateArguments update)
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(Path.Combine(update.Target, "SyncClipboard.Updater.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = update.Target };
            foreach (var argument in new[] { "--cleanup-work", update.WorkDirectory!, "--wait-pid",
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture), "--wait-start",
                current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) })
                start.ArgumentList.Add(argument);
            using var cleanup = Process.Start(start) ?? throw new IOException("Could not start updater cleanup.");
        }
        catch (Exception error)
        {
            Log(update, "Update succeeded, but workspace cleanup could not start: " + error);
        }
    }

    internal static async Task CleanupAsync(string workspace, int pid, long startTime)
    {
        ValidateWorkspace(workspace);
        await WaitForProcessAsync(pid, startTime, CancellationToken.None);
        ValidateWorkspace(workspace);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(workspace, true);
                return;
            }
            catch (Exception error) when (attempt < 9 && error is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(500);
            }
        }
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
