using Microsoft.UI.Xaml.Data;
using System;

namespace SyncClipboard.WinUI3.ValueConverters;

public class SettingTagToIcon : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string tag = (string)value;
        return tag switch
        {
            "SyncSetting" => "\uEBD3",
            "SystemSetting" => "\uE115",
            _ => "\uE115",
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

