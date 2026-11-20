using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test.Shared.History;

[TestClass]
public class HistoryTitlesTests
{
    [TestMethod]
    [DataRow(null, null)]
    [DataRow("", "")]
    [DataRow(" \r\n\t ", "")]
    [DataRow("  Cafe\u0301  ", "Café")]
    [DataRow("中文 👨‍👩‍👧‍👦", "中文 👨‍👩‍👧‍👦")]
    public void Normalize_PreservesStatesAndComposesUnicode(string? input, string? expected)
    {
        Assert.AreEqual(expected, HistoryTitles.Normalize(input));
    }

    [TestMethod]
    [DataRow("中")]
    [DataRow("😀")]
    [DataRow("e\u0301")]
    public void LengthLimit_CountsNormalizedCodePoints(string character)
    {
        var boundary = string.Concat(Enumerable.Repeat(character, 256));
        Assert.AreEqual(boundary.Normalize(), HistoryTitles.Normalize(" " + boundary + " "));
        var error = Assert.ThrowsExactly<HistoryTitleValidationException>(() => HistoryTitles.Normalize(boundary + character));
        Assert.AreEqual(HistoryTitleError.TooLong, error.Error);
    }

    [TestMethod]
    public void Normalize_ReportsMalformedText()
    {
        var error = Assert.ThrowsExactly<HistoryTitleValidationException>(() => HistoryTitles.Normalize(new string('\ud800', 1)));
        Assert.AreEqual(HistoryTitleError.InvalidCharacter, error.Error);
    }

    [TestMethod]
    [DataRow("a\nb", HistoryTitleError.LineBreak)]
    [DataRow("a\rb", HistoryTitleError.LineBreak)]
    [DataRow("a\tb", HistoryTitleError.ControlCharacter)]
    [DataRow("a\0b", HistoryTitleError.ControlCharacter)]
    [DataRow("a\u007fb", HistoryTitleError.ControlCharacter)]
    [DataRow("a\u2028b", HistoryTitleError.LineBreak)]
    [DataRow("a\u2029b", HistoryTitleError.LineBreak)]
    public void Normalize_RejectsInternalControlsAndLineSeparators(string title, HistoryTitleError expected)
    {
        var error = Assert.ThrowsExactly<HistoryTitleValidationException>(() => HistoryTitles.Normalize(title));
        Assert.AreEqual(expected, error.Error);
    }
}
