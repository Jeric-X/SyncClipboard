using System.Globalization;

namespace SyncClipboard.Updater;

internal sealed record UpdateArguments(
    string PackagePath,
    string Digest,
    string Target,
    int ProcessId,
    string Language,
    string[] ProtectedPaths,
    string? WorkDirectory = null,
    bool Elevated = false,
    long ProcessStartTime = 0,
    bool AppElevated = false)
{
    public static UpdateArguments Parse(string[] args)
    {
        var (values, protectedPaths, elevated) = ReadOptions(args);

        var digest = ParseDigest(GetRequiredValue(values, "--digest"));
        var processId = ParseProcessId(GetRequiredValue(values, "--process-id"));
        var target = Path.GetFullPath(GetRequiredValue(values, "--target"));
        target = Path.TrimEndingDirectorySeparator(target);
        var packagePath = Path.GetFullPath(GetRequiredValue(values, "--package-path"));

        var language = values.GetValueOrDefault("--language", "en");
        var workDirectory = values.GetValueOrDefault("--work-dir");
        if (workDirectory is not null)
        {
            workDirectory = Path.GetFullPath(workDirectory);
        }

        var startTimeValue = values.GetValueOrDefault("--process-start-time", "0");
        var processStartTime = long.Parse(startTimeValue, CultureInfo.InvariantCulture);
        var appElevated = ParseAppElevated(values.GetValueOrDefault("--app-elevated", "false"));

        return new UpdateArguments(
            PackagePath: packagePath,
            Digest: digest,
            Target: target,
            ProcessId: processId,
            Language: language,
            ProtectedPaths: protectedPaths,
            WorkDirectory: workDirectory,
            Elevated: elevated,
            ProcessStartTime: processStartTime,
            AppElevated: appElevated);
    }

    private static (Dictionary<string, string> Values, string[] ProtectedPaths, bool Elevated) ReadOptions(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var protectedPaths = new List<string>();
        var elevated = false;
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            switch (key)
            {
                case "--elevated" when !elevated:
                    elevated = true;
                    continue;
                case "--package-path":
                case "--digest":
                case "--target":
                case "--process-id":
                case "--language":
                case "--protect-path":
                case "--work-dir":
                case "--process-start-time":
                case "--app-elevated":
                    break;
                default:
                    throw new ArgumentException(UpdaterText.Current.UnknownArgument + key);
            }

            i++;
            if (i >= args.Length)
            {
                throw new ArgumentException(UpdaterText.Current.UnknownArgument + key);
            }

            var value = args[i];
            if (key == "--protect-path")
            {
                protectedPaths.Add(Path.GetFullPath(value));
            }
            else if (!values.TryAdd(key, value))
            {
                throw new ArgumentException(UpdaterText.Current.DuplicateArgument + key);
            }
        }

        return (values, [.. protectedPaths], elevated);
    }

    private static string GetRequiredValue(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(UpdaterText.Current.MissingArgument + key);
        }

        return value;
    }

    private static string ParseDigest(string value)
    {
        if (!value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(UpdaterText.Current.InvalidDigest);
        }

        if (value.Length != 71)
        {
            throw new ArgumentException(UpdaterText.Current.InvalidDigest);
        }

        if (!value[7..].All(Uri.IsHexDigit))
        {
            throw new ArgumentException(UpdaterText.Current.InvalidDigest);
        }

        return value;
    }

    private static int ParseProcessId(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
        {
            throw new ArgumentException(UpdaterText.Current.InvalidProcessId);
        }

        if (processId <= 0)
        {
            throw new ArgumentException(UpdaterText.Current.InvalidProcessId);
        }

        return processId;
    }

    private static bool ParseAppElevated(string value)
    {
        if (!bool.TryParse(value, out var appElevated))
        {
            throw new ArgumentException(UpdaterText.Current.InvalidAppElevated);
        }

        return appElevated;
    }

    public IEnumerable<string> ToCommandLine()
    {
        string[] args =
        [
            "--package-path", PackagePath,
            "--digest", Digest,
            "--target", Target,
            "--process-id", ProcessId.ToString(CultureInfo.InvariantCulture),
            "--app-elevated", AppElevated ? "true" : "false",
            "--language", Language
        ];

        foreach (var argument in args)
        {
            yield return argument;
        }

        foreach (var path in ProtectedPaths)
        {
            yield return "--protect-path";
            yield return path;
        }

        if (WorkDirectory is not null)
        {
            yield return "--work-dir";
            yield return WorkDirectory;
        }

        if (Elevated)
        {
            yield return "--elevated";
        }

        yield return "--process-start-time";
        yield return ProcessStartTime.ToString(CultureInfo.InvariantCulture);
    }
}
