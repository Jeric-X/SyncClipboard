using SyncClipboard.Core.Utilities.Attributes;
using System.Runtime.Serialization;

namespace SyncClipboard.Core.Utilities;

public static class EnumExtention
{
    public static string ToEnumMemberValue<TEnum>(this TEnum value) where TEnum : Enum
    {
        var enumType = typeof(TEnum);
        var name = Enum.GetName(enumType, value);
        if (name is null)
            return value.ToString();

        var platformAttribute = enumType.GetField(name)?.GetCustomAttributes(false).OfType<PlatformStringAttribute>().SingleOrDefault();
        if (platformAttribute is not null)
        {
            return platformAttribute.GetString();
        }

        var attribute = enumType.GetField(name)?.GetCustomAttributes(false).OfType<EnumMemberAttribute>().SingleOrDefault();
        if (attribute?.Value != null)
        {
            return attribute.Value;
        }

        return value.ToString();
    }
}
