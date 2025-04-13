namespace SyncClipboard.Core.Utilities.Attributes;

[AttributeUsage(AttributeTargets.All)]
public class PlatformStringAttribute(string defaultString) : Attribute
{
    public string Default { get; set; } = defaultString;
    public string? Mac { get; set; }
    public string? Linux { get; set; }
    public string? Windows { get; set; }

    public string GetString()
    {
        string? str = null;
        if (OperatingSystem.IsWindows())
        {
            str = Windows;
        }
        else if (OperatingSystem.IsLinux())
        {
            str = Linux;
        }
        else if (OperatingSystem.IsMacOS())
        {
            str = Mac;
        }

        return str ?? Default;
    }
}
