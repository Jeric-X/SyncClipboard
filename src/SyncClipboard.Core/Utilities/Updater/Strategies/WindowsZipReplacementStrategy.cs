using SyncClipboard.Core.Commons;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace SyncClipboard.Core.Utilities.Updater.Strategies;

internal sealed class WindowsZipReplacementStrategy : IFileReplacementStrategy
{
    public UpdatePackageKind Kind => UpdatePackageKind.WindowsPortable;

    public UpdateInstallCapability GetCapability()
        => GetCapability(Path.TrimEndingDirectorySeparator(Env.ProgramDirectory), Env.AppDataDirectory);

    internal static UpdateInstallCapability GetCapability(string target, string appDataDirectory)
    {
        // Protecting this data directory would exclude every installed file from replacement.
        if (UpdateFileSystem.IsWithin(target, appDataDirectory))
        {
            return new(UpdatePackageKind.Unsupported, string.Empty, I18n.Strings.UpdateLocationUnsupported);
        }
        return UpdateFileSystem.GetLocationCapability(UpdatePackageKind.WindowsPortable, target);
    }

    public string GetInstallationDirectory(string target) => target;

    public async Task<UpdateInstallTask> PreparePayloadAsync(UpdateInstallRequest snapshot, string work, CancellationToken token)
    {
        var target = snapshot.Capability.TargetPath;
        var stage = Path.Combine(work, "payload");
        var protectedPaths = new[] { Env.AppDataDirectory, Env.StaticConfigPath, Env.PortableUserConfigFile,
            Env.PortableAppDataDirectory, Env.RuntimeConfigPath, Env.AppDataPathConfigPath };
        Directory.CreateDirectory(stage);
        ExtractPackage(snapshot.PackagePath, stage, protectedPaths, target);
        var executable = Path.Combine(stage, "SyncClipboard.exe");
        ValidateExecutable(executable);
        ValidateVersion(FileVersionInfo.GetVersionInfo(executable).ProductVersion, snapshot.Version);
        UpdatePackageVerifier.ValidatePackageInfo(Path.Combine(stage, Env.UpdateInfoFile), Path.GetFileName(snapshot.PackagePath));
        var backupSize = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)
            .Select(path => Path.Combine(target, Path.GetRelativePath(stage, path)))
            .Where(File.Exists).Sum(path => new FileInfo(path).Length);
        UpdateFileSystem.CheckSpace(work, backupSize);
        await UpdateFileSystem.ExtractScriptAsync(work, "InstallWindowsZip.ps1", token);
        return new UpdateInstallTask
        {
            Directory = work,
            Kind = Kind.ToString(),
            Target = target,
            Stage = stage,
            Backup = Path.Combine(work, "backup"),
            Executable = Env.ProgramPath,
            Version = snapshot.Version,
            ProcessId = Environment.ProcessId,
            Elevate = !UpdateFileSystem.CanWrite(target),
            ProtectedPaths = protectedPaths
        };
    }

    public ProcessStartInfo CreateWorkerStartInfo(UpdateInstallTask update)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = true, WorkingDirectory = update.Directory };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(update.Directory, "InstallWindowsZip.ps1"), "-Work", update.Directory }) start.ArgumentList.Add(argument);
        return start;
    }

    internal static void ExtractPackage(string package, string destination, IEnumerable<string> protectedPaths, string target)
    {
        using var archive = ZipFile.OpenRead(package);
        UpdateFileSystem.CheckSpace(destination, archive.Entries.Sum(entry => entry.Length));
        var destinationPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = GetSafeEntryName(entry);
            var output = Path.GetFullPath(Path.Combine(destination, name));
            if (!output.StartsWith(destinationPrefix, pathComparison) || !seen.Add(output))
            {
                throw new InvalidDataException("Duplicate or invalid update archive entry: " + entry.FullName);
            }
            var installed = Path.Combine(target, name);
            if (protectedPaths.Any(path => UpdateFileSystem.IsWithin(installed, path)))
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

    internal static void ValidateVersion(string? productVersion, string expectedVersion)
    {
        if (productVersion is null || !AppVersion.TryParse(productVersion.Split('+')[0], out var actual)
            || !AppVersion.TryParse(expectedVersion, out var expected) || actual.CompareTo(expected) != 0)
        {
            throw new InvalidDataException("The update executable version does not match the release.");
        }
    }

    private static string GetSafeEntryName(ZipArchiveEntry entry)
    {
        // Validate Windows paths even when these tests run on Unix.
        var name = entry.FullName.Replace('\\', '/');
        if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p is ".." or "."
            || p.EndsWith(' ') || p.EndsWith('.')) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
        {
            throw new InvalidDataException("Unsafe update archive entry: " + entry.FullName);
        }
        return name;
    }

    private static void ValidateExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var machine = reader.PEHeaders.CoffHeader.Machine;
        var expected = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? Machine.Arm64 : Machine.Amd64;
        if (reader.PEHeaders.PEHeader is null || machine != expected)
        {
            throw new InvalidDataException("The update executable has an incompatible architecture.");
        }
    }
}
