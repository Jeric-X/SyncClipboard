using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Updater;

internal enum UpdateFailureAction { Abort, Retry, Rollback }

internal sealed class UpdateAbortedException(string? backupPath, Exception inner)
    : IOException(UpdaterText.Current.Aborted, inner)
{
    public string? BackupPath { get; } = backupPath;
}

internal sealed class UpdateRecoveryException(string backupPath, Exception original, IEnumerable<Exception> recoveryErrors)
    : AggregateException(UpdaterText.Current.RollbackFailed, new[] { original }.Concat(recoveryErrors))
{
    public string BackupPath { get; } = backupPath;
}

internal static class FileReplacement
{
    private sealed record Entry(string Source, string Destination, string Backup, bool Existed)
    {
        public bool Modified { get; set; }
        public FileAttributes Attributes { get; set; }
    }

    public static async Task ApplyAsync(string stage, string target, string backup, string[] protectedPaths,
        Action<string, int> progress, CancellationToken token,
        UpdateFailureHandler? onFailure = null)
    {
        Entry[] entries = [];
        long growth = 0;
        long largestFile = 0;
        await UpdateIo.RunAsync(UpdaterText.Current.PrepareBackup + backup, () =>
        {
            entries = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(path =>
                {
                    var relative = Path.GetRelativePath(stage, path);
                    var destination = Path.Combine(target, relative);
                    ValidateDestination(destination, target, protectedPaths);
                    return new Entry(path, destination, Path.Combine(backup, relative), File.Exists(destination));
                }).ToArray();
            Directory.CreateDirectory(backup);
            var backupBytes = entries.Where(e => e.Existed).Sum(e => new FileInfo(e.Destination).Length);
            WindowsZipPackage.CheckSpace(backup, backupBytes);
            growth = entries.Sum(e => Math.Max(0, new FileInfo(e.Source).Length
                - (e.Existed ? new FileInfo(e.Destination).Length : 0)));
            largestFile = entries.Length == 0 ? 0 : entries.Max(e => new FileInfo(e.Source).Length);
            WindowsZipPackage.CheckSpace(target, checked(growth + largestFile));
            return Task.CompletedTask;
        }, onFailure, token, backup);
        var createdDirectories = new List<string>();
        try
        {
            for (var i = 0; i < entries.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var entry = entries[i];
                await UpdateIo.RunAsync(UpdaterText.Current.BackUp + entry.Destination, async () =>
                {
                    ValidateDestination(entry.Destination, target, protectedPaths);
                    if (entry.Existed)
                    {
                        entry.Attributes = File.GetAttributes(entry.Destination) & ~FileAttributes.ReparsePoint;
                        Directory.CreateDirectory(Path.GetDirectoryName(entry.Backup)!);
                        await ReplaceAsync(entry.Destination, entry.Backup, onFailure, backup, token);
                    }
                }, onFailure, token, backup);
                Report(progress, "backup", (i + 1) * 100 / entries.Length);
            }
            // Backups may occupy the same drive as the installation.
            await UpdateIo.RunAsync(UpdaterText.Current.CheckFreeSpace + target, () =>
            {
                WindowsZipPackage.CheckSpace(target, checked(growth + largestFile));
                return Task.CompletedTask;
            }, onFailure, token, backup);
            for (var i = 0; i < entries.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var entry = entries[i];
                await UpdateIo.RunAsync(UpdaterText.Current.Replace + entry.Destination, async () =>
                {
                    ValidateDestination(entry.Destination, target, protectedPaths);
                    CreateParents(Path.GetDirectoryName(entry.Destination)!, createdDirectories);
                    await ReplaceAsync(entry.Source, entry.Destination, onFailure, backup, token,
                        () => entry.Modified = true);
                }, onFailure, token, backup, canRollback: true);
                Report(progress, "installing", (i + 1) * 100 / entries.Length);
                token.ThrowIfCancellationRequested();
            }
        }
        catch (Exception original) when (original is not UpdateAbortedException)
        {
            var errors = new List<Exception>();
            foreach (var entry in entries.Reverse().Where(e => e.Modified))
            {
                try
                {
                    Report(progress, "restoring", -1);
                    await UpdateIo.RunAsync(UpdaterText.Current.Restore + entry.Destination, async () =>
                    {
                        ValidateDestination(entry.Destination, target, protectedPaths);
                        if (entry.Existed)
                        {
                            await ReplaceAsync(entry.Backup, entry.Destination, onFailure, backup, CancellationToken.None);
                            File.SetAttributes(entry.Destination, entry.Attributes);
                        }
                        else
                            File.Delete(entry.Destination);
                    }, onFailure, CancellationToken.None, backup);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                    if (error is UpdateAbortedException)
                        throw new UpdateRecoveryException(backup, original, errors);
                }
            }
            foreach (var directory in createdDirectories.AsEnumerable().Reverse())
            {
                try
                {
                    await UpdateIo.RunAsync(UpdaterText.Current.RemoveDirectory + directory, () =>
                    {
                        if (!Directory.EnumerateFileSystemEntries(directory).Any())
                            Directory.Delete(directory);
                        return Task.CompletedTask;
                    }, onFailure, CancellationToken.None, backup);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                    if (error is UpdateAbortedException)
                        throw new UpdateRecoveryException(backup, original, errors);
                }
            }
            if (errors.Count != 0)
                throw new UpdateRecoveryException(backup, original, errors);
            if (original is UpdateRollbackException)
                throw new IOException(UpdaterText.Current.RolledBack, original);
            throw;
        }
    }

    internal static void ValidateDestination(string destination, string target, string[] protectedPaths)
    {
        if (!FileSystem.IsWithin(destination, target) || Directory.Exists(destination)
            || protectedPaths.Any(path => FileSystem.IsWithin(destination, path)))
            throw new IOException(UpdaterText.Current.UnsafeDestination + destination);
    }

    private static void CreateParents(string directory, List<string> created)
    {
        if (Directory.Exists(directory))
            return;
        CreateParents(Path.GetDirectoryName(directory)!, created);
        Directory.CreateDirectory(directory);
        created.Add(directory);
    }

    private static async Task ReplaceAsync(string source, string destination, UpdateFailureHandler? onFailure,
        string backup, CancellationToken token, Action? replaced = null)
    {
        // Test access and sharing without changing permissions, attributes, or file contents.
        if (File.Exists(destination))
        {
            using var writable = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.None);
        }
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".syncclipboard-" + Guid.NewGuid().ToString("N"));
        try
        {
            await WindowsZipPackage.CopyAsync(source, temporary, token);
            File.Move(temporary, destination, true);
            replaced?.Invoke();
        }
        finally
        {
            await UpdateIo.RunAsync(UpdaterText.Current.RemoveTemporaryFile + temporary, () =>
            {
                File.Delete(temporary);
                return Task.CompletedTask;
            }, onFailure, CancellationToken.None, backup);
        }
    }

    private static void Report(Action<string, int> progress, string phase, int percent)
    {
        try
        {
            progress(phase, percent);
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine(error);
        }
    }
}
