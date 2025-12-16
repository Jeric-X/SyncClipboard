namespace SyncClipboard.Core.ViewModels;

public class Converter
{
    public static string ServiceStatusToFontIcon(bool isError)
    {
        return isError ? "\uE10A" : "\uE17B";
    }

    public static string BoolToPasswordFontIcon(bool show)
    {
        return show ? "\uF78D" : "\uED1A";
    }

    public static string LimitUIText(string input)
    {
        const int MAX_LINES = 10;
        const int MAX_LINE_LENGTH = 500;
        if (input is null)
        {
            return string.Empty;
        }
        var lines = input.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
            .Take(11)
            .Select(line => line.Length > MAX_LINE_LENGTH ? line[..MAX_LINE_LENGTH] + "..." : line)
            .ToArray();
        if (lines.Length == MAX_LINES + 1)
        {
            lines[MAX_LINES] = "...";
        }
        input = string.Join(Environment.NewLine, lines);
        return input;
    }
}
