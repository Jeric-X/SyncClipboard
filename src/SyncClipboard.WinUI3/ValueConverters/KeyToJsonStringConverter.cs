using Microsoft.UI.Xaml.Data;
using SyncClipboard.Core.Models.Keyboard;
using System;
using System.Linq;
using System.Runtime.Serialization;

namespace SyncClipboard.WinUI3.ValueConverters;

public class KeyToJsonStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var enumType = typeof(Key);
        var name = Enum.GetName(typeof(Key), value);
        var attribute = enumType.GetField(name ?? "")?.GetCustomAttributes(false).OfType<EnumMemberAttribute>().SingleOrDefault();
        if (attribute?.Value != null)
        {
            return attribute.Value;
        }

        return value.ToString()!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}