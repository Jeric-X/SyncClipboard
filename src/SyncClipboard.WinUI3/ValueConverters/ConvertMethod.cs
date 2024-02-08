using Microsoft.UI.Xaml;

namespace SyncClipboard.WinUI3.ValueConverters;

internal static class ConvertMethod
{
    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }
}
