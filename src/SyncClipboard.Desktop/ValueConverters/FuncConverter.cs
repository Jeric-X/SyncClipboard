using System;
using System.Linq;
using Avalonia.Data.Converters;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.Models;

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

    public static FuncValueConverter<string?, string> SubStr { get; } =
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
}
