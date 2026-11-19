using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Utilities.Updater;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace SyncClipboard.Core.Utilities.Instances;

internal sealed record AppDataDirectoryOptions(string? AppDataDirectory)
{
    public const string AppDataArgument = "--app-data-dir";
    private static readonly Lazy<AppDataDirectoryOptions> _current = new(() => Parse(Environment.GetCommandLineArgs()[1..],
        OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("SYNC_CLIPBOARD_LAUNCH_DIRECTORY") : null)
        .PrepareDataDirectory(AppDomain.CurrentDomain.BaseDirectory));

    public static AppDataDirectoryOptions Current => _current.Value;
    public bool IsSpecifiedDataDirectory => AppDataDirectory is not null;

    public static AppDataDirectoryOptions Parse(string[] args, string? workingDirectory = null)
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

    internal AppDataDirectoryOptions PrepareDataDirectory(string programDirectory)
    {
        ValidateDataDirectory(programDirectory);
        if (AppDataDirectory is null)
            return this;
        Directory.CreateDirectory(AppDataDirectory);
        // Canonicalize after creation so the first launch uses the same identity as subsequent launches.
        return this with { AppDataDirectory = NormalizePath(AppDataDirectory) };
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
        if (OperatingSystem.IsMacOS())
            return NormalizeMacPath(fullPath);
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

    private static string NormalizeMacPath(string path)
    {
        var missingParts = new Stack<string>();
        var existing = path;
        while (!Path.Exists(existing))
        {
            missingParts.Push(Path.GetFileName(existing));
            existing = Path.GetDirectoryName(existing) ?? throw new DirectoryNotFoundException(path);
        }

        // macOS realpath restores the actual spelling on the volume, including case and symbolic links.
        var resolved = RealPath(existing, 0);
        if (resolved == 0)
            throw new IOException($"Could not resolve path: {existing}", new Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            var result = Marshal.PtrToStringUTF8(resolved)!;
            foreach (var part in missingParts)
                result = Path.Combine(result, part);
            return Path.TrimEndingDirectorySeparator(result);
        }
        finally
        {
            Free(resolved);
        }
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "realpath", SetLastError = true)]
    [SuppressMessage("Globalization", "CA2101:Specify marshaling for P/Invoke string arguments",
        Justification = "macOS realpath takes a UTF-8 char pointer, not a UTF-16 string.")]
    private static extern nint RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint resolvedPath);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "free")]
    private static extern void Free(nint pointer);

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
}
