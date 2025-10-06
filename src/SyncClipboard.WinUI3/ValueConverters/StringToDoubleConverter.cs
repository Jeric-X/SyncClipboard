using Microsoft.UI.Xaml.Data;
using System;

namespace SyncClipboard.WinUI3.ValueConverters;

public class StringToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string stringValue && double.TryParse(stringValue, out double result))
        {
            return result;
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value?.ToString() ?? "";
    }
}