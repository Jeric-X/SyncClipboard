namespace SyncClipboard.Core.Utilities.Attributes;

[AttributeUsage(AttributeTargets.All)]
public class PlatformStringAttribute : Attribute
{
    public string Default { get; set; } = "";
    public string? Mac { get; set; }
    public string? Linux { get; set; }
    public string? Windows { get; set; }

    public PlatformStringAttribute(string defaultString)
    {
        Default = defaultString;
    }

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
