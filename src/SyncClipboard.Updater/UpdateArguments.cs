using System.Globalization;

namespace SyncClipboard.Updater;

internal sealed record UpdateArguments(string PackagePath, string Digest, string Target, string Executable,
    string Version, int ProcessId, string Language, string[] ProtectedPaths, string? WorkDirectory = null,
    bool Elevated = false, int LauncherId = 0, long ProcessStartTime = 0)
{
    public static UpdateArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var protectedPaths = new List<string>();
        var elevated = false;
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key == "--elevated" && !elevated) { elevated = true; continue; }
            if (key is not ("--package-path" or "--digest" or "--target" or "--executable" or "--version"
                or "--process-id" or "--language" or "--protect-path" or "--work-dir" or "--launcher-id"
                or "--process-start-time") || ++i >= args.Length)
                throw new ArgumentException("Unknown or incomplete updater argument: " + key);
            if (key == "--protect-path") protectedPaths.Add(Path.GetFullPath(args[i]));
            else if (!values.TryAdd(key, args[i])) throw new ArgumentException("Duplicate updater argument: " + key);
        }

        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Missing updater argument: " + key);
        var digest = Required("--digest");
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71
            || !digest[7..].All(Uri.IsHexDigit)) throw new ArgumentException("Expected a SHA256 digest.");
        if (!int.TryParse(Required("--process-id"), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
            throw new ArgumentException("Invalid parent process ID.");
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Required("--target")));
        var executable = Path.GetFullPath(Required("--executable"));
        if (!string.Equals(executable, Path.Combine(target, "SyncClipboard.exe"), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The application executable must be SyncClipboard.exe in the installation directory.");
        return new(Path.GetFullPath(Required("--package-path")), digest, target, executable, Required("--version"), pid,
            values.GetValueOrDefault("--language", "en"), [.. protectedPaths],
            values.TryGetValue("--work-dir", out var work) ? Path.GetFullPath(work) : null, elevated,
            int.Parse(values.GetValueOrDefault("--launcher-id", "0"), CultureInfo.InvariantCulture),
            long.Parse(values.GetValueOrDefault("--process-start-time", "0"), CultureInfo.InvariantCulture));
    }

    public IEnumerable<string> ToCommandLine()
    {
        string[] args = ["--package-path", PackagePath, "--digest", Digest, "--target", Target,
            "--executable", Executable, "--version", Version, "--process-id", ProcessId.ToString(CultureInfo.InvariantCulture),
            "--language", Language];
        foreach (var argument in args) yield return argument;
        foreach (var path in ProtectedPaths) { yield return "--protect-path"; yield return path; }
        if (WorkDirectory is not null) { yield return "--work-dir"; yield return WorkDirectory; }
        if (Elevated) yield return "--elevated";
        yield return "--launcher-id";
        yield return LauncherId.ToString(CultureInfo.InvariantCulture);
        yield return "--process-start-time";
        yield return ProcessStartTime.ToString(CultureInfo.InvariantCulture);
    }
}
