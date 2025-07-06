using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.Models;

namespace SyncClipboard.WinUI3.ValueConverters;

internal static class ConvertMethod
{
    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static InfoBarSeverity ConvertSeverity(Severity severity)
    {
        return severity switch
        {
            Severity.Info => InfoBarSeverity.Informational,
            Severity.Success => InfoBarSeverity.Success,
            Severity.Warning => InfoBarSeverity.Warning,
            Severity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };
    }
}
