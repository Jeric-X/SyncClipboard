using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using System;
using System.Linq;

namespace SyncClipboard.WinUI3.ValueConverters;

internal static class ConvertMethod
{
    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility BoolToVisibilityNegate(bool value)
    {
        return value ? Visibility.Collapsed : Visibility.Visible;
    }

    public static bool BoolNegate(bool value)
    {
        return !value;
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

    public static string LimitLines(string input)
    {
        const int MAX_LINES = 10;
        if (input is null)
        {
            return string.Empty;
        }
        var lines = input.Split(["\r\n", "\r", "\n"], StringSplitOptions.None).Take(11).ToArray();
        if (lines.Length == MAX_LINES + 1)
        {
            lines[MAX_LINES] = "...";
        }
        input = string.Join(Environment.NewLine, lines);
        return input;
    }

    public static string ProfileTypeToFontIcon(ProfileType type)
    {
        return type switch
        {
            ProfileType.Text => "\uE8C1",
            ProfileType.File => "\uED43",
            ProfileType.Group => "\uED43",
            ProfileType.Image => "\uEB9F",
            _ => "\uF87E",
        };
    }

    public static string ToStarIcon(bool input)
    {
        return input ? "\uE735" : "\uE734";
    }

    public static string ToPinIcon(bool input)
    {
        return input ? "\uE841" : "\uE840";
    }

    public static SolidColorBrush SyncStateToBrush(SyncStatus state)
    {
        return state switch
        {
            SyncStatus.Disconnected => new SolidColorBrush(Colors.Orange),
            SyncStatus.Synced => new SolidColorBrush(Colors.ForestGreen),
            SyncStatus.SyncError => new SolidColorBrush(Colors.IndianRed),
            _ => new SolidColorBrush(Colors.Transparent),
        };
    }

    public static string SyncStateToText(SyncStatus state)
    {
        return I18nHelper.GetString(state);
    }

    public static double SyncStateToOpacity(SyncStatus state)
    {
        return state == SyncStatus.ServerOnly ? 0.5 : 1.0;
    }
}
