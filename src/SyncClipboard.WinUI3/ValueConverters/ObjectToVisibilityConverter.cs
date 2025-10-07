using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace SyncClipboard.WinUI3.ValueConverters;

public class ObjectToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}