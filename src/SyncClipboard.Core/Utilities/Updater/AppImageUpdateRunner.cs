using System.Diagnostics;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.Updater;

public record UpdateInstallProgress(string Phase, double? Percent = null);

// Used only by the isolated --install-update application. It does not create AppCore or load user data.
public sealed class AppImageUpdateRunner
{
    private readonly PreparedUpdate update;
    private readonly Action<string> launch;
    private readonly TimeSpan exitTimeout;

    public AppImageUpdateRunner(PreparedUpdate update) : this(update, Launch, TimeSpan.FromSeconds(60)) { }

    internal AppImageUpdateRunner(PreparedUpdate update, Action<string> launch, TimeSpan exitTimeout)
    {
        this.update = update;
        this.launch = launch;
        this.exitTimeout = exitTimeout;
    }

    public static PreparedUpdate Load(string manifest)
    {
        var update = JsonSerializer.Deserialize<PreparedUpdate>(File.ReadAllText(manifest))
            ?? throw new InvalidDataException("Invalid update task.");
        var work = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        if (update.Kind != nameof(UpdatePackageKind.AppImage) || Path.GetFullPath(update.Directory) != work
            || update.Stage != Path.Combine(work, "payload") || update.Backup != Path.Combine(work, "backup")
            || update.HelperExecutable != Path.Combine(work, "SyncClipboard-helper.AppImage")
            || !Path.IsPathRooted(update.Target) || update.Target != update.Executable
            || UpdateInstallFiles.IsWithin(update.Target, work) || UpdateInstallFiles.HasLinkedAncestor(work)
            || UpdateInstallFiles.HasLinkedAncestor(update.Target) || update.ProcessId <= 0
            || update.ProcessId == Environment.ProcessId)
        {
            throw new InvalidDataException("Unsafe AppImage update task.");
        }
        return update;
    }

    public async Task<bool> RunAsync(IProgress<UpdateInstallProgress> progress, CancellationToken token)
    {
        var backupComplete = false;
        var replacing = false;
        var parentExited = false;
        try
        {
            await WriteAsync("helper-pid", Environment.ProcessId.ToString());
            progress.Report(new("verifying"));
            await UpdateInstallFiles.VerifyHashAsync(update.Stage, update.Digest, token);
            if (File.Exists(update.Backup)) throw new IOException("The update backup already exists.");
            if (!UpdateInstallFiles.CanWrite(Path.GetDirectoryName(update.Target)!))
                throw new IOException(I18n.Strings.UpdateDirectoryNotWritable);
            UpdateInstallFiles.CheckSpace(update.Directory, new FileInfo(update.Target).Length);
            progress.Report(new("waiting"));
            await WriteAsync("ready", "");
            using (var handoff = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                handoff.CancelAfter(TimeSpan.FromMinutes(5));
                while (!File.Exists(Path.Combine(update.Directory, "commit")))
                {
                    ThrowIfCanceled(token);
                    await Task.Delay(100, handoff.Token);
                }
            }
            ThrowIfCanceled(token);
            try
            {
                using var parent = Process.GetProcessById(update.ProcessId);
                await parent.WaitForExitAsync(token).WaitAsync(exitTimeout, token);
            }
            catch (ArgumentException) { } // The original application has already exited.
            catch (TimeoutException) { throw new IOException("Timed out waiting for SyncClipboard to exit."); }
            parentExited = true;
            ThrowIfCanceled(token);
            await CopyAsync(update.Target, update.Backup, "backup", progress, token);
            backupComplete = true;
            ThrowIfCanceled(token);
            replacing = true;
            File.Delete(update.Target);
            await CopyAsync(update.Stage, update.Target, "installing", progress, token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(update.Target,
                File.GetUnixFileMode(update.Backup) | UnixFileMode.UserExecute);
            await WriteAsync("installed", "");
            await WriteAsync("completed", "");
            progress.Report(new("starting"));
            launch(update.Target);
            return true;
        }
        catch (Exception ex)
        {
            return await HandleFailureAsync(ex, backupComplete, replacing, parentExited, progress);
        }
    }

    private async Task<bool> HandleFailureAsync(Exception error, bool backupComplete, bool replacing, bool parentExited,
        IProgress<UpdateInstallProgress> progress)
    {
        var failure = error.ToString();
        if (error is OperationCanceledException && !replacing) await WriteAsync("canceled", "");
        if (replacing && backupComplete)
        {
            try
            {
                File.Delete(update.Target);
                await CopyAsync(update.Backup, update.Target, "restoring", progress, CancellationToken.None);
                await WriteAsync("restored", "");
            }
            catch (Exception restoreError) { failure += "\nRollback failed: " + restoreError; }
        }
        await File.AppendAllTextAsync(Path.Combine(update.Directory, "install.log"), failure + "\n", CancellationToken.None);
        await WriteAsync("failed", failure);
        if (parentExited && (!replacing || File.Exists(Path.Combine(update.Directory, "restored"))))
        {
            try { launch(update.Target); }
            catch (Exception launchError)
            {
                await File.AppendAllTextAsync(Path.Combine(update.Directory, "install.log"), launchError + "\n", CancellationToken.None);
            }
        }
        progress.Report(new("failed"));
        return false;
    }

    private void ThrowIfCanceled(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (File.Exists(Path.Combine(update.Directory, "cancel"))) throw new OperationCanceledException();
    }

    private Task WriteAsync(string name, string value)
        => File.WriteAllTextAsync(Path.Combine(update.Directory, name), value, CancellationToken.None);

    private static async Task CopyAsync(string source, string destination, string phase,
        IProgress<UpdateInstallProgress> progress, CancellationToken token)
    {
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
        {
            var buffer = new byte[1024 * 1024];
            var timer = Stopwatch.StartNew();
            progress.Report(new(phase, 0));
            int count;
            long copied = 0;
            while ((count = await input.ReadAsync(buffer, token)) != 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), token);
                copied += count;
                if (timer.ElapsedMilliseconds < 100) continue;
                progress.Report(new(phase, Math.Min(99, copied * 100d / input.Length)));
                timer.Restart();
            }
            await output.FlushAsync(token);
        }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
        progress.Report(new(phase, 100));
    }

    internal static ProcessStartInfo CreateLaunchInfo(string executable)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)
        };
        foreach (var name in new[] { "APPIMAGE", "APPDIR", "ARGV0", "OWD", "LD_LIBRARY_PATH", "LD_PRELOAD" })
            start.Environment.Remove(name);
        return start;
    }

    private static void Launch(string path)
    {
        using var process = Process.Start(CreateLaunchInfo(path)) ?? throw new IOException("Could not restart SyncClipboard.");
    }
}
