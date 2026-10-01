using System.Diagnostics;

namespace SyncClipboard.Core.Utilities.Updater;

internal static class LinuxUpdaterFiles
{
    internal static readonly string[] Libraries = ["libSkiaSharp.so", "libHarfBuzzSharp.so"];

    public static string? GetAppImagePath()
    {
        var path = Environment.GetEnvironmentVariable("APPIMAGE");
        return path is not null && Path.IsPathFullyQualified(path) && File.Exists(path) ? path : null;
    }

    public static string[] GetFiles(string programDirectory)
    {
        string[] names = ["SyncClipboard.Updater", .. Libraries];
        var files = names.Select(name => Path.Combine(programDirectory, name)).ToArray();
        return files.All(File.Exists) ? files : [];
    }

    public static void ConfigureEnvironment(ProcessStartInfo start)
    {
        start.Environment.TryGetValue("APPDIR", out var appDir);
        if (!string.IsNullOrEmpty(appDir))
        {
            foreach (var key in new[] { "LD_LIBRARY_PATH", "PATH", "XDG_DATA_DIRS" })
            {
                if (start.Environment.TryGetValue(key, out var value) && value is not null)
                {
                    var paths = value.Split(':').Where(path => !Path.IsPathFullyQualified(path) || !FileSystem.IsWithin(path, appDir));
                    start.Environment[key] = string.Join(':', paths);
                }
            }
        }
        foreach (var key in new[] { "APPDIR", "APPIMAGE", "ARGV0", "OWD" })
        {
            start.Environment.Remove(key);
        }
    }
}
