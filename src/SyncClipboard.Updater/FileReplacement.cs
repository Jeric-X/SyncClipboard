using SyncClipboard.Core.Utilities;
using System.Security.AccessControl;

namespace SyncClipboard.Updater;

internal enum UpdateFailureAction { Abort, Retry, Rollback }

internal sealed class UpdateAbortedException(string backupPath, Exception inner)
    : IOException("Update terminated without rollback. / 更新已终止，未执行回滚。", inner)
{
    public string BackupPath { get; } = backupPath;
}

internal sealed class UpdateRecoveryException(string backupPath, Exception original, IEnumerable<Exception> recoveryErrors)
    : AggregateException("Update rollback failed. / 更新回滚失败。", new[] { original }.Concat(recoveryErrors))
{
    public string BackupPath { get; } = backupPath;
}

internal static class FileReplacement
{
    private sealed record Entry(string Source, string Destination, string Backup, bool Existed)
    {
        public bool Modified { get; set; }
        public FileAttributes Attributes { get; set; }
        public byte[]? Security { get; set; }
    }

    public static async Task ApplyAsync(string stage, string target, string backup, string[] protectedPaths,
        Action<string, int> progress, CancellationToken token,
        Func<string, Exception, CancellationToken, Task<UpdateFailureAction>>? onFailure = null)
    {
        var entries = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
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
        var growth = entries.Sum(e => Math.Max(0, new FileInfo(e.Source).Length
            - (e.Existed ? new FileInfo(e.Destination).Length : 0)));
        var largestFile = entries.Length == 0 ? 0 : entries.Max(e => new FileInfo(e.Source).Length);
        WindowsZipPackage.CheckSpace(target, checked(growth + largestFile));
        var createdDirectories = new List<string>();
        var rollbackRequested = false;

        async Task PerformAsync(string path, Func<Task> operation)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await operation();
                    return;
                }
                catch (Exception error) when (onFailure is not null && error is IOException or UnauthorizedAccessException)
                {
                    var action = await onFailure(path, error, token);
                    token.ThrowIfCancellationRequested();
                    if (action == UpdateFailureAction.Retry)
                        continue;
                    if (action == UpdateFailureAction.Abort)
                        throw new UpdateAbortedException(backup, error);
                    rollbackRequested = true;
                    throw;
                }
            }
        }
        try
        {
            for (var i = 0; i < entries.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var entry = entries[i];
                await PerformAsync(entry.Destination, async () =>
                {
                    ValidateDestination(entry.Destination, target, protectedPaths);
                    if (entry.Existed)
                    {
                        entry.Attributes = File.GetAttributes(entry.Destination) & ~FileAttributes.ReparsePoint;
                        if (OperatingSystem.IsWindows())
                            entry.Security = new FileInfo(entry.Destination).GetAccessControl().GetSecurityDescriptorBinaryForm();
                        Directory.CreateDirectory(Path.GetDirectoryName(entry.Backup)!);
                        await ReplaceAsync(entry.Destination, entry.Backup, token);
                    }
                });
                Report(progress, "backup", (i + 1) * 100 / entries.Length);
            }
            // Backups may occupy the same drive as the installation.
            WindowsZipPackage.CheckSpace(target, checked(growth + largestFile));
            for (var i = 0; i < entries.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var entry = entries[i];
                await PerformAsync(entry.Destination, async () =>
                {
                    ValidateDestination(entry.Destination, target, protectedPaths);
                    CreateParents(Path.GetDirectoryName(entry.Destination)!, createdDirectories);
                    await ReplaceAsync(entry.Source, entry.Destination, token);
                    entry.Modified = true;
                });
                Report(progress, "installing", (i + 1) * 100 / entries.Length);
            }
            token.ThrowIfCancellationRequested();
        }
        catch (Exception original) when (original is not UpdateAbortedException)
        {
            var errors = new List<Exception>();
            foreach (var entry in entries.Reverse().Where(e => e.Modified))
            {
                try
                {
                    Report(progress, "restoring", -1);
                    ValidateDestination(entry.Destination, target, protectedPaths);
                    if (entry.Existed)
                    {
                        await ReplaceAsync(entry.Backup, entry.Destination, CancellationToken.None);
                        if (OperatingSystem.IsWindows() && entry.Security is not null)
                        {
                            var security = new FileSecurity();
                            security.SetSecurityDescriptorBinaryForm(entry.Security,
                                AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);
                            new FileInfo(entry.Destination).SetAccessControl(security);
                        }
                        File.SetAttributes(entry.Destination, entry.Attributes);
                    }
                    else
                        File.Delete(entry.Destination);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            foreach (var directory in createdDirectories.AsEnumerable().Reverse())
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            if (errors.Count != 0)
                throw new UpdateRecoveryException(backup, original, errors);
            if (rollbackRequested)
                throw new IOException("Update rolled back. / 更新已回滚。", original);
            throw;
        }
    }

    internal static void ValidateDestination(string destination, string target, string[] protectedPaths)
    {
        if (!FileSystem.IsWithin(destination, target) || Directory.Exists(destination)
            || protectedPaths.Any(path => FileSystem.IsWithin(destination, path)))
            throw new IOException("Unsafe update destination: " + destination);
    }

    private static void CreateParents(string directory, List<string> created)
    {
        if (Directory.Exists(directory))
            return;
        CreateParents(Path.GetDirectoryName(directory)!, created);
        Directory.CreateDirectory(directory);
        created.Add(directory);
    }

    private static async Task ReplaceAsync(string source, string destination, CancellationToken token)
    {
        // Test write access and sharing before replacing an existing file; never truncate it in place.
        if (File.Exists(destination))
        {
            using var writable = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.None);
        }
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".syncclipboard-" + Guid.NewGuid().ToString("N"));
        try
        {
            await WindowsZipPackage.CopyAsync(source, temporary, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally
        {
            // A failed cleanup must not hide the replacement result or bypass rollback.
            try
            {
                File.Delete(temporary);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
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
