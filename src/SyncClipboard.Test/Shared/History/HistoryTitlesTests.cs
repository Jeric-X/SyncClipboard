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
        Assert.ThrowsExactly<ArgumentException>(() => HistoryTitles.Normalize(boundary + character));
    }

    [TestMethod]
    [DataRow("a\nb")]
    [DataRow("a\rb")]
    [DataRow("a\tb")]
    [DataRow("a\0b")]
    [DataRow("a\u007fb")]
    [DataRow("a\u2028b")]
    [DataRow("a\u2029b")]
    public void Normalize_RejectsInternalControlsAndLineSeparators(string title)
    {
        Assert.ThrowsExactly<ArgumentException>(() => HistoryTitles.Normalize(title));
    }
}
