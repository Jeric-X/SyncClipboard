using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Updater;

internal sealed class UpdateRecoveryException(Exception original, IEnumerable<Exception> recoveryErrors)
    : AggregateException("Update rollback failed. Keep the updater workspace and restore the backup before restarting.",
        new[] { original }.Concat(recoveryErrors));

internal static class FileReplacement
{
    private sealed record Entry(string Source, string Destination, string Backup, bool Existed)
    {
        public bool Modified { get; set; }
    }

    public static async Task ApplyAsync(string stage, string target, string backup, string[] protectedPaths,
        Action<string, int> progress, CancellationToken token)
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
        try
        {
            for (var i = 0; i < entries.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var entry = entries[i];
                ValidateDestination(entry.Destination, target, protectedPaths);
                if (entry.Existed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.Backup)!);
                    await WindowsZipPackage.CopyAsync(entry.Destination, entry.Backup, token);
                }
                Report(progress, "backup", (i + 1) * 100 / entries.Length);
            }
            // Backups may occupy the same drive as the installation.
            WindowsZipPackage.CheckSpace(target, checked(growth + largestFile));
            for (var i = 0; i < entries.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var entry = entries[i];
                ValidateDestination(entry.Destination, target, protectedPaths);
                CreateParents(Path.GetDirectoryName(entry.Destination)!, createdDirectories);
                await ReplaceAsync(entry.Source, entry.Destination, token);
                entry.Modified = true;
                Report(progress, "installing", (i + 1) * 100 / entries.Length);
            }
            token.ThrowIfCancellationRequested();
        }
        catch (Exception original)
        {
            var errors = new List<Exception>();
            foreach (var entry in entries.Reverse().Where(e => e.Modified))
            {
                try
                {
                    Report(progress, "restoring", -1);
                    ValidateDestination(entry.Destination, target, protectedPaths);
                    if (entry.Existed) await ReplaceAsync(entry.Backup, entry.Destination, CancellationToken.None);
                    else File.Delete(entry.Destination);
                }
                catch (Exception error) { errors.Add(error); }
            }
            foreach (var directory in createdDirectories.AsEnumerable().Reverse())
            {
                try { if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
                catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count != 0) throw new UpdateRecoveryException(original, errors);
            throw;
        }
    }

    internal static void ValidateDestination(string destination, string target, string[] protectedPaths)
    {
        if (!FileSystem.IsWithin(destination, target) || Directory.Exists(destination)
            || FileSystem.HasLinkedAncestor(destination)
            || protectedPaths.Any(path => FileSystem.IsWithin(destination, path)))
            throw new IOException("Unsafe update destination: " + destination);
    }

    private static void CreateParents(string directory, List<string> created)
    {
        if (Directory.Exists(directory)) return;
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
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void Report(Action<string, int> progress, string phase, int percent)
    {
        try { progress(phase, percent); }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
    }
}
