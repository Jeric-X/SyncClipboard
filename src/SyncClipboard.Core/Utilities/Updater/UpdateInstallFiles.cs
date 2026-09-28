using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Cryptography;

namespace SyncClipboard.Core.Utilities.Updater;

public static class UpdateInstallFiles
{
    public static bool IsWithin(string path, string directory)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    public static async Task VerifyHashAsync(string path, string digest, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var actual = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(I18n.Strings.HashMismatch);
        }
    }

    public static void CheckSpace(string directory, long bytes)
    {
        var fullPath = Path.GetFullPath(directory);
        var drive = DriveInfo.GetDrives().Where(d => d.IsReady && IsWithin(fullPath, d.RootDirectory.FullName))
            .OrderByDescending(d => d.RootDirectory.FullName.Length).FirstOrDefault();
        if (drive is not null && drive.AvailableFreeSpace < bytes + (32L * 1024 * 1024))
        {
            throw new IOException(I18n.Strings.UpdateInsufficientSpace);
        }
    }

    internal static long GetSize(string path) => Directory.Exists(path)
        ? Directory.EnumerateFiles(path, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).Sum(file => new FileInfo(file).Length)
        : new FileInfo(path).Length;

    public static bool CanWrite(string directory)
    {
        var probe = Path.Combine(directory, ".syncclipboard-write-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void ExtractPortable(string package, string destination, IEnumerable<string> protectedPaths, string target)
    {
        using var archive = ZipFile.OpenRead(package);
        CheckSpace(destination, archive.Entries.Sum(entry => entry.Length));
        var destinationPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            // Validate Windows paths even when these tests run on Unix.
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p is ".." or "."
                || p.EndsWith(' ') || p.EndsWith('.')) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                throw new InvalidDataException("Unsafe update archive entry: " + entry.FullName);
            }
            var output = Path.GetFullPath(Path.Combine(destination, name));
            if (!output.StartsWith(destinationPrefix, pathComparison) || !seen.Add(output))
            {
                throw new InvalidDataException("Duplicate or invalid update archive entry: " + entry.FullName);
            }
            var installed = Path.Combine(target, name);
            if (protectedPaths.Any(path => IsWithin(installed, path)))
            {
                continue;
            }
            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(output);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                entry.ExtractToFile(output);
            }
        }
        if (!File.Exists(Path.Combine(destination, "SyncClipboard.exe")))
        {
            throw new InvalidDataException("The update archive does not contain SyncClipboard.exe.");
        }
    }

    internal static bool HasLinkedAncestor(string path)
    {
        for (FileSystemInfo? item = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            item is not null; item = item is DirectoryInfo dir ? dir.Parent : ((FileInfo)item).Directory)
        {
            // macOS /var and /tmp are system symlinks; installation locations themselves must be unambiguous.
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    internal static void ValidateWindowsExecutable(string path, bool checkArchitecture = true)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var machine = reader.PEHeaders.CoffHeader.Machine;
        var expected = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? Machine.Arm64 : Machine.Amd64;
        if (reader.PEHeaders.PEHeader is null || (checkArchitecture && machine != expected))
        {
            throw new InvalidDataException("The update executable has an incompatible architecture.");
        }
    }

    internal static void ValidatePackageInfo(string path, string packageName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("UpdateInfo").GetProperty("package_name").GetString() != packageName)
        {
            throw new InvalidDataException("The update package does not match this installation.");
        }
    }

    internal static UpdateInstallCapability GetLocationCapability(UpdatePackageKind kind, string? target)
    {
        if (target is null || target.Any(char.IsControl) || (!Directory.Exists(target) && !File.Exists(target)) || HasLinkedAncestor(target))
        {
            return new(UpdatePackageKind.Unsupported, string.Empty, I18n.Strings.UpdateLocationUnsupported);
        }
        return new(kind, target);
    }
}
