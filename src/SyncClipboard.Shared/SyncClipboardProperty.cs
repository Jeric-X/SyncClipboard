using System.Reflection;

namespace SyncClipboard.Shared;

public static class SyncClipboardProperty
{
    public static string AppVersion { get; } = GetAppVersion();

    private static string GetAppVersion()
    {
        var informationalVersion = typeof(SyncClipboardProperty).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return typeof(SyncClipboardProperty).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var metadataSeparator = informationalVersion.IndexOf('+');
        return metadataSeparator >= 0
            ? informationalVersion[..metadataSeparator]
            : informationalVersion;
    }
}
