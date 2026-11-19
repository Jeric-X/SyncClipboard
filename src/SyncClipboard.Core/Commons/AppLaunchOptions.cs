using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Updater;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SyncClipboard.Core.Commons;

internal sealed record AppLaunchOptions(string? AppDataDirectory)
{
    public const string AppDataArgument = "--app-data-dir";
    private static readonly Lazy<AppLaunchOptions> _current = new(() => Parse(Environment.GetCommandLineArgs()[1..],
        OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("SYNC_CLIPBOARD_LAUNCH_DIRECTORY") : null));

    public static AppLaunchOptions Current => _current.Value;
    public bool IsSpecifiedDataDirectory => AppDataDirectory is not null;

    public string InstanceSuffix => AppDataDirectory is null ? string.Empty
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            OperatingSystem.IsWindows() ? AppDataDirectory.ToUpperInvariant() : AppDataDirectory)))[..24];

    // Keep the Unix socket name short enough for the standard macOS per-user temporary directory.
    public string PipeName => "SC-" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Environment.UserName + ":" + InstanceSuffix)))[..16];

    public static AppLaunchOptions Parse(string[] args, string? workingDirectory = null)
    {
        string? directory = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == AppDataArgument || args[i].StartsWith(AppDataArgument + "=", StringComparison.Ordinal))
            {
                if (directory is not null)
                    throw new ArgumentException($"Duplicate argument: {AppDataArgument}");
                if (args[i] == AppDataArgument)
                {
                    if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"{AppDataArgument} requires a directory.");
                    directory = args[i];
                }
                else
                {
                    directory = args[i][(AppDataArgument.Length + 1)..];
                }
                if (string.IsNullOrWhiteSpace(directory))
                    throw new ArgumentException($"{AppDataArgument} requires a directory.");
            }
        }

        return new(directory is null ? null : NormalizeDirectory(directory, workingDirectory));
    }

    private static string NormalizeDirectory(string path, string? workingDirectory)
    {
        var directory = NormalizePath(Path.GetFullPath(path, workingDirectory ?? Environment.CurrentDirectory));
        if (File.Exists(directory))
            throw new ArgumentException($"The application data path is not a directory: {directory}");
        return directory;
    }

    internal static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var current = Path.GetPathRoot(fullPath)!;
        foreach (var part in fullPath[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo? entry = Directory.Exists(current) ? new DirectoryInfo(current)
                : File.Exists(current) ? new FileInfo(current) : null;
            var target = entry?.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
                current = NormalizePath(target.FullName);
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    public void ValidateDataDirectory(string programDirectory)
    {
        if (AppDataDirectory is null)
            return;
        var normalizedProgramDirectory = NormalizePath(programDirectory);
        if (OperatingSystem.IsMacOS() && MacUpdaterFiles.FindBundle(normalizedProgramDirectory) is string bundle
            && FileSystem.IsWithin(AppDataDirectory, bundle))
        {
            throw new ArgumentException("Choose an application data directory outside the .app bundle.");
        }
        if (string.Equals(AppDataDirectory, normalizedProgramDirectory,
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Choose a separate application data directory instead of the program directory.");
        }
    }

    public void AddRestartArguments(ProcessStartInfo startInfo)
    {
        startInfo.ArgumentList.Add(StartArguments.ShutdownPrivious);
        if (AppDataDirectory is not null)
        {
            startInfo.ArgumentList.Add(AppDataArgument);
            startInfo.ArgumentList.Add(AppDataDirectory);
        }
    }
}
