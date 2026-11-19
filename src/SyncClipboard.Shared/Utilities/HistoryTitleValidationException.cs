namespace SyncClipboard.Shared.Utilities;

public enum HistoryTitleError
{
    TooLong,
    LineBreak,
    ControlCharacter,
    InvalidCharacter
}

public sealed class HistoryTitleValidationException(HistoryTitleError error) : ArgumentException(error switch
{
    HistoryTitleError.TooLong => $"Use at most {HistoryTitles.MaxLength} characters.",
    HistoryTitleError.LineBreak => "Line breaks are not allowed.",
    HistoryTitleError.ControlCharacter => "Control characters are not allowed.",
    _ => "Contains invalid characters."
})
{
    public HistoryTitleError Error { get; } = error;
}
