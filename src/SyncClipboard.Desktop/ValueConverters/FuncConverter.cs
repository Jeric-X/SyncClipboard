using Avalonia.Data.Converters;
using Avalonia.Media;
using SyncClipboard.Core.I18n;
using Avalonia.Media.Imaging;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.Models;
using System;
using Avalonia;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.ValueConverters;

public class FuncConverter
{
    public static FuncValueConverter<Severity?, InfoBarSeverity> ConvertSeverity { get; } =
        new FuncValueConverter<Severity?, InfoBarSeverity>(severity => severity switch
        {
            Severity.Info => InfoBarSeverity.Informational,
            Severity.Success => InfoBarSeverity.Success,
            Severity.Warning => InfoBarSeverity.Warning,
            Severity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        });

    public static FuncValueConverter<string?, string> LimitLines { get; } =
        new FuncValueConverter<string?, string>(input =>
        {
            if (input is null)
            {
                return string.Empty;
            }
            return Converter.LimitUIText(input);
        });

    public static FuncValueConverter<ProfileType?, string> ProfileTypeToFontIcon { get; } =
        new FuncValueConverter<ProfileType?, string>(input =>
        {
            return input switch
            {
                ProfileType.Text => "\uE164",
                ProfileType.File => "\uED43",
                ProfileType.Group => "\uED43",
                ProfileType.Image => char.ConvertFromUtf32((int)Convert.ToUInt32("F807F", 16)).ToString(),
                _ => "\uE10A",
            };
        });

    public static FuncValueConverter<string, Bitmap?> ToBitImage { get; } =
        new FuncValueConverter<string, Bitmap?>(input =>
        {
            try
            {
                return new Bitmap(input!);
            }
            catch { }
            return null;
        });

    public static FuncValueConverter<bool, string> ToStarIcon { get; } =
        new FuncValueConverter<bool, string>(input =>
        {
            if (input)
            {
                return "\uE1CF";
            }
            return "\uE1CE";
        });

    public static FuncValueConverter<bool, string> ToPinIcon { get; } =
        new FuncValueConverter<bool, string>(input =>
        {
            if (input)
            {
                return char.ConvertFromUtf32((int)Convert.ToUInt32("F809B", 16)).ToString();
            }
            return "\uE141";
        });

    public static FuncValueConverter<double, Thickness> CalculateInfoBarMargin { get; } =
        new FuncValueConverter<double, Thickness>(listBoxHeight =>
        {
            // 计算距离底部20%的位置作为Margin的Bottom值
            var bottomMargin = listBoxHeight * 0.2;
            return new Thickness(0, 0, 0, bottomMargin);
        });

    public static FuncValueConverter<SyncStatus, IBrush> SyncStateToBrush { get; } =
        new FuncValueConverter<SyncStatus, IBrush>(state =>
        {
            return state switch
            {
                SyncStatus.Disconnected => Brushes.Orange,
                SyncStatus.SyncError => Brushes.IndianRed,
                SyncStatus.Synced => Brushes.ForestGreen,
                _ => Brushes.Transparent,
            };
        });

    public static FuncValueConverter<SyncStatus, string> SyncStateToText { get; } =
        new FuncValueConverter<SyncStatus, string>(I18nHelper.GetString);

    public static FuncValueConverter<SyncStatus, double> SyncStateToOpacity { get; } =
        new FuncValueConverter<SyncStatus, double>(state =>
        {
            return state == SyncStatus.ServerOnly ? 0.5 : 1.0;
        });
}
