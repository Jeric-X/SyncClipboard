using Avalonia.Data.Converters;
using SyncClipboard.Core.ViewModels;
using System;
using System.Globalization;

namespace SyncClipboard.Desktop.ValueConverters;

public class BoolToPasswordIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var show = value as bool?;
        if (!show.HasValue)
        {
            throw new ArgumentException("value is not type bool", nameof(value));
        }
        return Converter.BoolToPasswordFontIcon(show!.Value);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}