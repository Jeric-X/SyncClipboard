using SyncClipboard.Core.Utilities;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SyncClipboard.Updater;

internal static class UpdateWorker
{
    private const string WorkspaceMarker = ".syncclipboard-update";
    private const int RecoveryFailed = 2;

    public static async Task<int> RunAsync(UpdateArguments update, CancellationToken token)
    {
        if (update.WorkDirectory is null) return await RelocateAsync(update, token);
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
                catch (IOException) { canRestart = false; throw new IOException("Another updater is using this installation."); }
            }

            try { await InstallAsync(update, token); }
            catch (UnauthorizedAccessException) when (!update.Elevated && !token.IsCancellationRequested)
            {
                Log(update, "Retrying with administrator permission after restoring the original files.");
                var start = CreateStartInfo(Environment.ProcessPath!, update with { Elevated = true });
                start.UseShellExecute = true;
                start.Verb = "runas";
                using var elevated = Process.Start(start) ?? throw new IOException("Could not start the elevated updater.");
                // Do not abandon a worker that might be replacing files. Its own console handles cancellation.
                await elevated.WaitForExitAsync(CancellationToken.None);
                if (elevated.ExitCode == RecoveryFailed)
                {
                    canRestart = false;
                    throw new IOException("The elevated update could not restore every file. Keep the backup for recovery.");
                }
                if (elevated.ExitCode != 0) throw new IOException("The elevated update failed. See install.log for details.");
            }
            if (update.Elevated) return 0;

            Restart(update);
            Log(update, "Update completed. Removing staging files and old backups.");
            ScheduleCleanup(update);
            return 0;
        }
        catch (Exception error)
        {
            targetLock?.Dispose();
            targetLock = null;
            Log(update, error.ToString());
            var recoveryFailed = error is UpdateRecoveryException || !canRestart;
            if (!update.Elevated && !recoveryFailed) await RestartIfStoppedAsync(update);
            ShowFailure(update, error);
            return recoveryFailed ? RecoveryFailed : 1;
        }
        finally { targetLock?.Dispose(); }
    }

    private static async Task<int> RelocateAsync(UpdateArguments update, CancellationToken token)
    {
        string? workspace = null;
        try
        {
            WindowsZipPackage.ValidateTarget(update);
            var root = Path.Combine(Path.GetTempPath(), "SyncClipboard-updates");
            Directory.CreateDirectory(root);
            if (FileSystem.HasLinkedAncestor(root)) throw new IOException("The updater workspace contains a link.");
            workspace = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            File.WriteAllText(Path.Combine(workspace, WorkspaceMarker), "SyncClipboard updater workspace v1");
            var worker = Path.Combine(workspace, "SyncClipboard.Updater.exe");
            await WindowsZipPackage.CopyAsync(Environment.ProcessPath!, worker, token);
            token.ThrowIfCancellationRequested();
            using var process = Process.Start(CreateStartInfo(worker,
                update with { WorkDirectory = workspace, LauncherId = Environment.ProcessId }))
                ?? throw new IOException("Could not start the relocated updater.");
            return 0;
        }
        catch (Exception error)
        {
            if (workspace is not null) Log(update with { WorkDirectory = workspace }, error.ToString());
            await RestartIfStoppedAsync(update);
            ShowFailure(update, error);
            return 1;
        }
    }

    private static async Task InstallAsync(UpdateArguments update, CancellationToken token)
    {
        var attempt = Path.Combine(update.WorkDirectory!, "attempt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(attempt);
        Report(update, "preparing", -1);
        var stage = await WindowsZipPackage.PrepareAsync(update, attempt, token);
        Report(update, "waiting", -1);
        await WaitForProcessAsync(update.ProcessId, update.ProcessStartTime, token);
        await WaitForProcessAsync(update.LauncherId, 0, token);
        await FileReplacement.ApplyAsync(stage, update.Target, Path.Combine(attempt, "backup"),
            WindowsZipPackage.GetProtectedPaths(update), (phase, percent) => Report(update, phase, percent), token);
    }

    internal static async Task WaitForProcessAsync(int pid, long startTime, CancellationToken token)
    {
        if (pid <= 0) return;
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (process)
        {
            try
            {
                if (process.HasExited) return;
                if (startTime != 0 && process.StartTime.ToUniversalTime().Ticks != startTime) return;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { throw new IOException("Timed out waiting for SyncClipboard to exit."); }
            }
            catch (InvalidOperationException) when (process.HasExited) { }
        }
    }

    private static async Task RestartIfStoppedAsync(UpdateArguments update)
    {
        try
        {
            await WaitForProcessAsync(update.ProcessId, update.ProcessStartTime, CancellationToken.None);
            Restart(update);
        }
        catch (Exception error) { Log(update, "Could not restart the application: " + error); }
    }

    private static void Restart(UpdateArguments update)
    {
        using var process = Process.Start(new ProcessStartInfo(update.Executable)
        { UseShellExecute = true, WorkingDirectory = update.Target }) ?? throw new IOException("Could not restart SyncClipboard.");
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, UpdateArguments update)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in update.ToCommandLine()) start.ArgumentList.Add(argument);
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
                current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) }) start.ArgumentList.Add(argument);
            using var cleanup = Process.Start(start) ?? throw new IOException("Could not start updater cleanup.");
        }
        catch (Exception error) { Log(update, "Update succeeded, but workspace cleanup could not start: " + error); }
    }

    internal static async Task CleanupAsync(string workspace, int pid, long startTime)
    {
        ValidateWorkspace(workspace);
        await WaitForProcessAsync(pid, startTime, CancellationToken.None);
        ValidateWorkspace(workspace);
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Delete(workspace, true); return; }
            catch (Exception error) when (attempt < 9 && error is IOException or UnauthorizedAccessException)
            { await Task.Delay(500); }
        }
    }

    internal static void ValidateWorkspace(string workspace)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(workspace));
        if (directory.Parent?.Name != "SyncClipboard-updates" || !Guid.TryParseExact(directory.Name, "N", out _)
            || FileSystem.HasLinkedAncestor(directory.FullName)
            || !File.Exists(Path.Combine(directory.FullName, WorkspaceMarker)))
            throw new IOException("Invalid updater workspace.");
    }

    private static void Log(UpdateArguments update, string message)
    {
        if (update.WorkDirectory is null) return;
        try { File.AppendAllText(Path.Combine(update.WorkDirectory, "install.log"), $"{DateTime.UtcNow:O} {message}\n"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Debug.WriteLine(error); }
    }

    private static void Report(UpdateArguments update, string phase, int percent)
    {
        var text = update.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? phase switch
        {
            "preparing" => "复制并校验更新包",
            "waiting" => "等待程序退出",
            "backup" => "备份程序文件",
            "installing" => "安装更新",
            "restoring" => "恢复旧版本",
            _ => phase
        } : phase;
        try { Console.WriteLine(percent < 0 ? text : $"{text}: {percent}%"); }
        catch (IOException error) { Log(update, error.Message); }
    }

    private static void ShowFailure(UpdateArguments update, Exception error)
    {
        try
        {
            Console.Error.WriteLine(error is Win32Exception { NativeErrorCode: 1223 } ? "Update authorization canceled." : error.Message);
            if (update.WorkDirectory is not null) Console.Error.WriteLine("Log and backup: " + update.WorkDirectory);
            if (!update.Elevated && !Console.IsInputRedirected)
            {
                Console.Error.WriteLine("Press Enter to close. / 按回车关闭。");
                Console.ReadLine();
            }
        }
        catch (IOException displayError) { Log(update, displayError.ToString()); }
    }
}
