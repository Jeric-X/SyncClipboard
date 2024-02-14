using Avalonia.Data.Converters;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Utilities;
using System;
using System.Globalization;

namespace SyncClipboard.Desktop.ValueConverters;

public class KeyToJsonStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Key key)
            return null;

        return key.ToEnumMemberValue();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}