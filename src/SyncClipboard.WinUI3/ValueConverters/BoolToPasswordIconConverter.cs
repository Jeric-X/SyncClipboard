using Microsoft.UI.Xaml.Data;
using System;

namespace SyncClipboard.WinUI3.ValueConverters;

public class BoolToPasswordIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isChecked = (bool)value;
        return isChecked ? "\uF78D" : "\uED1A";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}