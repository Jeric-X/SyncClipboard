using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SyncClipboard.Desktop.ValueConverters;

public class BoolToFontIcon : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isError = value as bool?;
        ArgumentNullException.ThrowIfNull(nameof(isError));
        return isError!.Value ? "\uEA39" : "\uEC76"; ;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

