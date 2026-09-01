using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SyncClipboard.Core.ViewModels.EmbeddedIcons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;
using System;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Core.ViewModels.Sub;

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

    public static bool Not(bool value)
    {
        return !value;
    }

    public static Visibility ObjectToVisibility(object? value)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility NullToVisibility(object? value)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility TextPreviewVisibility(HistoryRecordVM? record, string? previewImage)
    {
        if (record == null)
            return Visibility.Collapsed;
        return record.Type != ProfileType.Image || previewImage == null
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility ImagePreviewVisibility(HistoryRecordVM? record, string? previewImage)
    {
        if (record == null)
            return Visibility.Collapsed;
        return record.Type == ProfileType.Image && previewImage != null
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public static string ProfileTypeToString(ProfileType? type)
    {
        if (type == null)
            return string.Empty;
        return Converter.ProfileTypeToLocalizedString(type.Value);
    }

    public static ProfileType? GetProfileType(HistoryRecordVM? record)
    {
        return record?.Type;
    }

    public static string GetRelativeTime(HistoryRecordVM? record)
    {
        return record?.RelativeTime ?? string.Empty;
    }

    public static string GetText(HistoryRecordVM? record)
    {
        return record?.Text ?? string.Empty;
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

    public static string LimitHistoryListText(string? input, bool isCompactListMode)
    {
        return Converter.LimitHistoryListText(input, isCompactListMode);
    }

    public static string GetRecordSize(HistoryRecordVM? record)
    {
        return Converter.GetRecordSize(record);
    }

    public static Geometry HistoryRecordToHugeiconsGeometry(ProfileType type, string[]? filePaths)
    {
        var pathData = HugeiconsEmbeddedIconProvider.ResolvePathData(type, filePaths);
        return (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), pathData);
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

    public static double LocalFileReadyToOpacity(bool isLocalFileReady)
    {
        return isLocalFileReady ? 1.0 : 0.5;
    }

    public static BitmapImage? CreateBitmap(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return null;
        }

        return new BitmapImage(new Uri(uri));
    }
}
