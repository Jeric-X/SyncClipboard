using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Updater.Zip;

internal static class FileReplacement
{
    private sealed record Entry(string Source, string Destination, string Backup, bool Existed)
    {
        public bool Modified { get; set; }
        public FileAttributes Attributes { get; set; }
    }

    private sealed record Conflict(string Destination, string Backup, bool IsDirectory)
    {
        public bool Modified { get; set; }
    }

    public static async Task ApplyAsync(string stage, string target, string backup, string[] protectedPaths,
        Action<string, int> progress, CancellationToken token,
        UpdateFailureHandler? onFailure = null, Action<bool>? setRollbackAvailable = null)
    {
        Entry[] entries = [];
        Conflict[] conflicts = [];
        string[] directories = [];
        long growth = 0;
        long largestFile = 0;
        await InteractiveOperation.RunAsync(UpdaterText.Current.PrepareBackup + backup, () =>
        {
            entries = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(path =>
                {
                    var relative = Path.GetRelativePath(stage, path);
                    var destination = Path.Combine(target, relative);
                    ValidateDestination(destination, target, protectedPaths);
                    return new Entry(path, destination, Path.Combine(backup, relative), File.Exists(destination));
                }).ToArray();
            directories = Directory.EnumerateDirectories(stage, "*", SearchOption.AllDirectories)
                .Select(path => Path.Combine(target, Path.GetRelativePath(stage, path))).ToArray();
            foreach (var directory in directories)
            {
                ValidateDestination(directory, target, protectedPaths);
            }
            conflicts = FindConflicts(entries, directories, target, backup, protectedPaths);
            Directory.CreateDirectory(backup);
            var backupBytes = entries.Where(e => e.Existed).Sum(e => new FileInfo(e.Destination).Length)
                + conflicts.Sum(conflict => FileSystem.GetSize(conflict.Destination));
            PackageFiles.CheckSpace(backup, backupBytes);
            growth = entries.Sum(e => Math.Max(0, new FileInfo(e.Source).Length
                - (e.Existed ? new FileInfo(e.Destination).Length : 0)));
            largestFile = entries.Length == 0 ? 0 : entries.Max(e => new FileInfo(e.Source).Length);
            PackageFiles.CheckSpace(target, checked(growth + largestFile));
            return Task.CompletedTask;
        }, onFailure, token, backup);
        var createdDirectories = new List<string>();
        try
        {
            await BackUpConflictsAsync(conflicts, backup, onFailure, token);
            await BackUpFilesAsync(entries, target, backup, protectedPaths, progress, onFailure, token);
            // Backups may occupy the same drive as the installation.
            await InteractiveOperation.RunAsync(UpdaterText.Current.CheckFreeSpace + target, () =>
            {
                PackageFiles.CheckSpace(target, checked(growth + largestFile));
                return Task.CompletedTask;
            }, onFailure, token, backup);
            await RemoveConflictsAsync(conflicts, backup, onFailure, setRollbackAvailable, token);
            bool CanRollback() => createdDirectories.Count != 0 || conflicts.Any(conflict => conflict.Modified);
            await CreateDirectoriesAsync(directories, createdDirectories, backup, onFailure, setRollbackAvailable, CanRollback, token);
            await InstallFilesAsync(entries, target, backup, protectedPaths, createdDirectories, progress,
                onFailure, setRollbackAvailable, CanRollback, token);
        }
        catch (Exception original) when (original is not UpdateAbortedException)
        {
            setRollbackAvailable?.Invoke(false);
            var errors = new List<Exception>();
            await RestoreFilesAsync(entries, target, backup, protectedPaths, progress, onFailure, original, errors);
            await RemoveCreatedDirectoriesAsync(createdDirectories, backup, onFailure, original, errors);
            await RestoreConflictsAsync(conflicts, backup, onFailure, original, errors);
            if (errors.Count != 0)
                throw new UpdateRecoveryException(backup, original, errors);
            if (original is UpdateRollbackException)
                throw new IOException(UpdaterText.Current.RolledBack, original);
            throw;
        }
        finally
        {
            setRollbackAvailable?.Invoke(false);
        }
    }

    private static async Task BackUpFilesAsync(Entry[] entries, string target, string backup, string[] protectedPaths,
        Action<string, int> progress, UpdateFailureHandler? onFailure, CancellationToken token)
    {
        for (var i = 0; i < entries.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = entries[i];
            await InteractiveOperation.RunAsync(UpdaterText.Current.BackUp + entry.Destination, async () =>
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
    }

    private static async Task CreateDirectoriesAsync(string[] directories, List<string> createdDirectories, string backup,
        UpdateFailureHandler? onFailure, Action<bool>? setRollbackAvailable, Func<bool> canRollback, CancellationToken token)
    {
        foreach (var directory in directories)
        {
            token.ThrowIfCancellationRequested();
            await InteractiveOperation.RunAsync(UpdaterText.Current.Replace + directory, () =>
            {
                var count = createdDirectories.Count;
                CreateParents(directory, createdDirectories);
                if (createdDirectories.Count != count)
                    setRollbackAvailable?.Invoke(true);
                return Task.CompletedTask;
            }, onFailure, token, backup, canRollback);
        }
    }

    private static async Task InstallFilesAsync(Entry[] entries, string target, string backup, string[] protectedPaths,
        List<string> createdDirectories, Action<string, int> progress, UpdateFailureHandler? onFailure,
        Action<bool>? setRollbackAvailable, Func<bool> hasModifiedConflicts, CancellationToken token)
    {
        for (var i = 0; i < entries.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = entries[i];
            await InteractiveOperation.RunAsync(UpdaterText.Current.Replace + entry.Destination, async () =>
            {
                ValidateDestination(entry.Destination, target, protectedPaths);
                CreateParents(Path.GetDirectoryName(entry.Destination)!, createdDirectories);
                await ReplaceAsync(entry.Source, entry.Destination, onFailure, backup, token,
                    () =>
                    {
                        entry.Modified = true;
                        setRollbackAvailable?.Invoke(true);
                    });
            }, onFailure, token, backup, canRollback: () => hasModifiedConflicts() || entries.Any(e => e.Modified));
            Report(progress, "installing", (i + 1) * 100 / entries.Length);
            token.ThrowIfCancellationRequested();
        }
    }

    private static async Task RestoreFilesAsync(Entry[] entries, string target, string backup, string[] protectedPaths,
        Action<string, int> progress, UpdateFailureHandler? onFailure, Exception original, List<Exception> errors)
    {
        foreach (var entry in entries.Reverse().Where(e => e.Modified))
        {
            try
            {
                Report(progress, "restoring", -1);
                await InteractiveOperation.RunAsync(UpdaterText.Current.Restore + entry.Destination, async () =>
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
    }

    private static async Task RemoveCreatedDirectoriesAsync(List<string> createdDirectories, string backup,
        UpdateFailureHandler? onFailure, Exception original, List<Exception> errors)
    {
        foreach (var directory in createdDirectories.AsEnumerable().Reverse())
        {
            try
            {
                await InteractiveOperation.RunAsync(UpdaterText.Current.RemoveDirectory + directory, () =>
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
    }

    private static Conflict[] FindConflicts(Entry[] entries, string[] directories, string target, string backup,
        string[] protectedPaths)
    {
        var paths = entries.Where(entry => Directory.Exists(entry.Destination))
            .Select(entry => entry.Destination).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parents = entries.Select(entry => Path.GetDirectoryName(entry.Destination)!).Concat(directories);
        foreach (var directory in parents)
        {
            var parent = directory;
            while (FileSystem.IsWithin(parent, target) && Path.GetRelativePath(target, parent) != ".")
            {
                if (File.Exists(parent))
                    paths.Add(parent);
                parent = Path.GetDirectoryName(parent)!;
            }
        }
        foreach (var path in paths)
        {
            if (protectedPaths.Any(protectedPath => FileSystem.IsWithin(protectedPath, path)
                || FileSystem.IsWithin(path, protectedPath)))
                throw new IOException(UpdaterText.Current.UnsafeDestination + path);
        }
        return paths.Select(path => new Conflict(path, Path.Combine(backup, Path.GetRelativePath(target, path)),
            Directory.Exists(path))).ToArray();
    }

    private static async Task BackUpConflictsAsync(Conflict[] conflicts, string backup,
        UpdateFailureHandler? onFailure, CancellationToken token)
    {
        foreach (var conflict in conflicts)
        {
            token.ThrowIfCancellationRequested();
            await InteractiveOperation.RunAsync(UpdaterText.Current.BackUp + conflict.Destination, async () =>
            {
                if (conflict.IsDirectory)
                    await CopyDirectoryAsync(conflict.Destination, conflict.Backup, token);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(conflict.Backup)!);
                    await ReplaceAsync(conflict.Destination, conflict.Backup, onFailure, backup, token);
                }
            }, onFailure, token, backup);
        }
    }

    private static async Task RemoveConflictsAsync(Conflict[] conflicts, string backup, UpdateFailureHandler? onFailure,
        Action<bool>? setRollbackAvailable, CancellationToken token)
    {
        foreach (var conflict in conflicts)
        {
            token.ThrowIfCancellationRequested();
            await InteractiveOperation.RunAsync(UpdaterText.Current.Replace + conflict.Destination, () =>
            {
                // Directory deletion can fail after removing only some of its contents.
                conflict.Modified = true;
                setRollbackAvailable?.Invoke(true);
                DeletePath(conflict.Destination);
                return Task.CompletedTask;
            }, onFailure, token, backup, canRollback: () => conflicts.Any(item => item.Modified));
        }
    }

    private static async Task RestoreConflictsAsync(Conflict[] conflicts, string backup, UpdateFailureHandler? onFailure,
        Exception original, List<Exception> errors)
    {
        foreach (var conflict in conflicts.Reverse().Where(item => item.Modified))
        {
            try
            {
                await InteractiveOperation.RunAsync(UpdaterText.Current.Restore + conflict.Destination, async () =>
                {
                    DeletePath(conflict.Destination);
                    if (conflict.IsDirectory)
                        await CopyDirectoryAsync(conflict.Backup, conflict.Destination, CancellationToken.None);
                    else
                        await ReplaceAsync(conflict.Backup, conflict.Destination, onFailure, backup, CancellationToken.None);
                }, onFailure, CancellationToken.None, backup);
            }
            catch (Exception error)
            {
                errors.Add(error);
                if (error is UpdateAbortedException)
                    throw new UpdateRecoveryException(backup, original, errors);
            }
        }
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var output = Path.Combine(destination, Path.GetRelativePath(source, file));
            await ReplaceAsync(file, output, null, destination, token);
        }
    }

    private static void DeletePath(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
        else
            File.Delete(path);
    }

    internal static void ValidateDestination(string destination, string target, string[] protectedPaths)
    {
        if (!FileSystem.IsWithin(destination, target)
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
            await PackageFiles.CopyAsync(source, temporary, token);
            File.Move(temporary, destination, true);
            replaced?.Invoke();
        }
        finally
        {
            await InteractiveOperation.RunAsync(UpdaterText.Current.RemoveTemporaryFile + temporary, () =>
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
