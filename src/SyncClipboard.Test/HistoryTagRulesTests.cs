using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryTagRulesTests
{
    [TestMethod]
    public void NormalizeNames_TrimsAndNormalizesUnicodeWithoutMergingCase()
    {
        string[] expected = ["Prompt", "prompt", "é", "工作"];
        CollectionAssert.AreEqual(expected,
            HistoryTags.NormalizeNames([" 工作 ", "Prompt", "prompt", "e\u0301", "é", "工作"]));
    }

    [TestMethod]
    public void NormalizeName_LimitsNormalizedUnicodeScalarsRatherThanUtf16Units()
    {
        var ascii = new string('a', 64);
        var emoji = string.Concat(Enumerable.Repeat("😀", 64));
        var decomposed = string.Concat(Enumerable.Repeat("e\u0301", 64));
        Assert.AreEqual(ascii, HistoryTags.NormalizeName(ascii));
        Assert.AreEqual(emoji, HistoryTags.NormalizeName(emoji));
        Assert.AreEqual(new string('é', 64), HistoryTags.NormalizeName(decomposed));
        Assert.ThrowsExactly<ArgumentException>(() => HistoryTags.NormalizeName(ascii + "a"));
        Assert.ThrowsExactly<ArgumentException>(() => HistoryTags.NormalizeName(emoji + "😀"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("\t")]
    [DataRow("work\nnotes")]
    [DataRow("work\rnotes")]
    [DataRow("work\tnotes")]
    [DataRow("work\0notes")]
    [DataRow("work\u2028notes")]
    [DataRow("work\u2029notes")]
    public void NormalizeName_RejectsEmptyNamesAndEmbeddedControlOrLineBreakCharacters(string name) =>
        Assert.ThrowsExactly<ArgumentException>(() => HistoryTags.NormalizeName(name));

    [TestMethod]
    public void NormalizeName_RejectsNull() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => HistoryTags.NormalizeName(null!));

    [TestMethod]
    public void Normalize_EnforcesRecordLimitAfterNormalizationAndDeduplication()
    {
        var names = Enumerable.Range(0, 32).Select(i => $"tag{i:D2}").ToArray();
        CollectionAssert.AreEqual(names, HistoryTags.Normalize(names.Concat(names.Select(name => $" {name} "))));
        Assert.ThrowsExactly<ArgumentException>(() => HistoryTags.Normalize(names.Append("extra")));
        Assert.IsEmpty(HistoryTags.Normalize([]));
    }

    [TestMethod]
    public void NormalizeNames_AllowsCatalogsSpanningMoreThanOneRecord()
    {
        var names = Enumerable.Range(0, 40).Select(i => $"tag{i:D2}").ToArray();
        CollectionAssert.AreEqual(names, HistoryTags.NormalizeNames(names.Reverse()));
    }

    [TestMethod]
    public void Apply_NormalizesEditsPreservesUntouchedTagsAndDoesNotMutateInputs()
    {
        string[] current = ["keep", "é", "Work", "work"];
        string[] added = [" 新标签 ", "keep"];
        string[] removed = ["e\u0301", "work", "absent"];
        var originalCurrent = current.ToArray();
        var originalAdded = added.ToArray();
        var originalRemoved = removed.ToArray();
        string[] expected = ["Work", "keep", "新标签"];
        CollectionAssert.AreEqual(expected, HistoryTags.Apply(current, added, removed));
        CollectionAssert.AreEqual(originalCurrent, current);
        CollectionAssert.AreEqual(originalAdded, added);
        CollectionAssert.AreEqual(originalRemoved, removed);
    }

    [TestMethod]
    public void Apply_RemovesEquivalentNamesFromNoncanonicalCurrentTags()
    {
        Assert.IsEmpty(HistoryTags.Apply(["e\u0301", " 工作 ", "é"], [], ["é", "工作"]));
        Assert.IsEmpty(HistoryTags.Apply(["e\u0301"], [], ["e\u0301"]));
    }

    [TestMethod]
    public void Apply_AdditionWinsWhenTheSameNormalizedTagIsAlsoRemoved()
    {
        string[] expected = ["keep", "é"];
        CollectionAssert.AreEqual(expected, HistoryTags.Apply(["keep", "é"], [" e\u0301 "], ["é"]));
    }

    [TestMethod]
    public void Apply_LimitsTheFinalRecordRatherThanTheBatchOfRemovalNames()
    {
        var current = Enumerable.Range(0, 32).Select(i => $"old{i:D2}").ToArray();
        var added = Enumerable.Range(0, 32).Select(i => $"new{i:D2}").ToArray();
        CollectionAssert.AreEqual(added, HistoryTags.Apply(current, added, current.Append("absent")));
        Assert.IsEmpty(HistoryTags.Apply(current, [], current.Append("absent")));
        Assert.ThrowsExactly<ArgumentException>(() => HistoryTags.Apply(current, ["extra"], []));
    }

    [TestMethod]
    public void Apply_RejectsInvalidAddedAndRemovedNames()
    {
        Assert.ThrowsExactly<ArgumentException>(() => HistoryTags.Apply(["keep"], [""], []));
        Assert.ThrowsExactly<ArgumentException>(() => HistoryTags.Apply(["keep"], [], ["bad\nname"]));
    }
}
