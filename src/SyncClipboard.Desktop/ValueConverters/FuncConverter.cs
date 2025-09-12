using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting.Unicode;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Models;
using System;
using System.Linq;

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
}
