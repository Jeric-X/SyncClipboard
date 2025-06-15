using Avalonia.Data.Converters;
using SyncClipboard.Core.ViewModels;
using System;
using System.Globalization;

namespace SyncClipboard.Desktop.ValueConverters;

public class BoolToFontIcon : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isError = value as bool?;
        if (!isError.HasValue)
        {
            throw new ArgumentException("value is not type bool", nameof(value));
        }
        return Converter.ServiceStatusToFontIcon(isError!.Value);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}