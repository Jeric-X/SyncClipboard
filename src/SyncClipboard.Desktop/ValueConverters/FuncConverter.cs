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
}
