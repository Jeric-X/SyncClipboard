namespace SyncClipboard.Shared.Models;

public static class DateTimePropertyHelper
{
    public static DateTime GetDateTimeProperty(ref DateTime field)
    {
        if (field.Kind == DateTimeKind.Unspecified)
        {
            field = DateTime.SpecifyKind(field, DateTimeKind.Utc);
        }
        return field;
    }

    public static void SetDateTimeProperty(DateTime value, ref DateTime field)
    {
        if (value.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException("DateTime kind is unspecified.");
        }

        field = value.ToUniversalTime();
    }
}