using Microsoft.UI.Xaml.Data;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Utilities;
using System;

namespace SyncClipboard.WinUI3.ValueConverters;

public class KeyToJsonStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not Key key)
            return null!;

        return key.ToEnumMemberValue();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}