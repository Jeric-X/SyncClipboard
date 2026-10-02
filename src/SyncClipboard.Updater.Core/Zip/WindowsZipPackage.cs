using SyncClipboard.Core.Utilities;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SyncClipboard.Updater.Zip;

internal static class WindowsZipPackage
{
    internal static readonly string[] UserFiles = ["appdata", "StaticConfig.json", "SyncClipboard.json", "RuntimeConfig.json"];

    public static string[] GetProtectedPaths(UpdateArguments update)
        => [.. update.ProtectedPaths, .. UserFiles.Select(name => Path.Combine(update.Target, name))];

    public static void ValidateTarget(UpdateArguments update)
    {
        if (!Directory.Exists(update.Target))
            throw new IOException(UpdaterText.Current.MissingTarget);
        if (GetProtectedPaths(update).Any(path => FileSystem.IsWithin(update.Target, path)))
            throw new IOException(UpdaterText.Current.ProtectedTarget);
        if (update.WorkDirectory is not null && FileSystem.IsWithin(update.WorkDirectory, update.Target))
            throw new IOException(UpdaterText.Current.WorkspaceInsideTarget);
    }

    public static async Task<string> PrepareAsync(UpdateArguments update, string attempt, CancellationToken token)
    {
        ValidateTarget(update);
        PackageFiles.CheckSpace(attempt, new FileInfo(update.PackagePath).Length);
        var snapshot = Path.Combine(attempt, "package.zip");
        await PackageFiles.CopyAsync(update.PackagePath, snapshot, token);
        await PackageFiles.VerifyHashAsync(snapshot, update.Digest, token);
        var stage = Path.Combine(attempt, "payload");
        Directory.CreateDirectory(stage);
        await ExtractAsync(snapshot, stage, update.Target, GetProtectedPaths(update), token);
        ValidatePayload(stage, Path.GetFileName(update.PackagePath));
        return stage;
    }

    internal static async Task ExtractAsync(string package, string stage, string target,
        string[] protectedPaths, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(package);
        PackageFiles.CheckSpace(stage, archive.Entries.Aggregate(0L, (size, entry) => checked(size + entry.Length)));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = ValidateEntryName(entry);
            var relative = name.TrimEnd('/');
            if (!seen.Add(relative))
                throw new InvalidDataException(UpdaterText.Current.DuplicateArchiveEntry + name);
            if (protectedPaths.Any(path => FileSystem.IsWithin(Path.Combine(target, relative), path)))
                continue;
            var output = Path.GetFullPath(Path.Combine(stage, relative));
            if (!FileSystem.IsWithin(output, stage) || output == stage)
                throw new InvalidDataException(UpdaterText.Current.InvalidArchivePath);
            if (name.EndsWith('/'))
                Directory.CreateDirectory(output);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await using var input = entry.Open();
                await using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(file, token);
            }
        }
    }

    private static string ValidateEntryName(ZipArchiveEntry entry)
    {
        var name = entry.FullName.Replace('\\', '/');
        if (string.IsNullOrEmpty(name) || name.StartsWith('/') || name.Contains(':')
            || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            throw new InvalidDataException(UpdaterText.Current.UnsafeArchiveEntry + entry.FullName);
        foreach (var part in name.TrimEnd('/').Split('/'))
        {
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (string.IsNullOrEmpty(part) || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')
                || part.Any(c => char.IsControl(c) || "<>\"|?*".Contains(c))
                || stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
                || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal)
                    || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3])))
                throw new InvalidDataException(UpdaterText.Current.UnsafeWindowsArchiveEntry + entry.FullName);
        }
        return name;
    }

    internal static void ValidatePayload(string stage, string packageName)
    {
        var executable = GetExecutablePath(stage);
        ValidateArchitecture(executable);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "update_info.json")));
        var info = document.RootElement.GetProperty("UpdateInfo");
        if (info.GetProperty("manage_type").GetString() != "manual" || info.GetProperty("update_src").GetString() != "github"
            || info.GetProperty("package_name").GetString() != packageName)
            throw new InvalidDataException(UpdaterText.Current.PackageMismatch);
    }

    internal static string GetExecutablePath(string directory)
    {
        foreach (var name in new[] { "SyncClipboard.exe", "SyncClipboard.Desktop.Default.exe" })
        {
            var executable = Path.Combine(directory, name);
            if (File.Exists(executable))
                return executable;
        }
        throw new FileNotFoundException(UpdaterText.Current.InvalidArchivePath + directory);
    }

    private static void ValidateArchitecture(string executable)
    {
        using var stream = File.OpenRead(executable);
        using var reader = new PEReader(stream);
        var machine = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? Machine.Arm64 : Machine.Amd64;
        if (reader.PEHeaders.PEHeader is null || reader.PEHeaders.CoffHeader.Machine != machine)
            throw new InvalidDataException(UpdaterText.Current.IncompatibleArchitecture + Path.GetFileName(executable));
    }
}
