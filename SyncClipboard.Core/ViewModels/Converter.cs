namespace SyncClipboard.Core.ViewModels;

public class Converter
{
    public static string ServiceStatusToFontIcon(bool isError)
    {
        return isError ? "\uE10A" : "\uE17B";
    }
}
