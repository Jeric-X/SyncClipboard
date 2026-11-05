using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryTagOverflowLayoutTests
{
    [TestMethod]
    public void LineSpacing_SeparatesRowsWithoutAddingOuterPadding()
    {
        var tags = Enumerable.Repeat(new HistoryTagSize(70, 30), 4).ToArray();
        var layout = HistoryTagOverflowLayout.Calculate(tags, new(40, 24), 160, 66, lineSpacing: 6);
        Assert.HasCount(4, layout.Tags);
        Assert.AreEqual(0, layout.Tags[0].Y);
        Assert.AreEqual(36, layout.Tags[2].Y);
        Assert.AreEqual(66, layout.Height);
        var overflow = HistoryTagOverflowLayout.Calculate(tags, new(40, 24), 160, 65, lineSpacing: 6);
        Assert.HasCount(2, overflow.Tags);
        Assert.AreEqual(new HistoryTagPlacement(120, 36, 40, 24), overflow.More);
        Assert.AreEqual(60, overflow.Height);
        var singleRow = HistoryTagOverflowLayout.Calculate(tags, new(40, 24), 160, 55, lineSpacing: 6);
        Assert.HasCount(1, singleRow.Tags);
        Assert.AreEqual(new HistoryTagPlacement(120, 3, 40, 24), singleRow.More);
        Assert.AreEqual(30, singleRow.Height);
    }

    [TestMethod]
    public void Overflow_ReservesAnEntireChipWithinTheAvailableHeight()
    {
        HistoryTagSize[] tags = [new(70, 30), new(70, 30), new(70, 30), new(70, 30)];
        var shortRow = HistoryTagOverflowLayout.Calculate(tags, new(40, 30), 160, 40);
        Assert.HasCount(1, shortRow.Tags);
        Assert.AreEqual(new HistoryTagPlacement(120, 0, 40, 30), shortRow.More);
        var tallerRow = HistoryTagOverflowLayout.Calculate(tags, new(40, 30), 160, 60);
        Assert.HasCount(4, tallerRow.Tags);
        Assert.IsNull(tallerRow.More);
        Assert.AreEqual(60, tallerRow.Height);
        Assert.AreEqual(160, tallerRow.Tags[1].X + tallerRow.Tags[1].Width);
        Assert.AreEqual(160, tallerRow.Tags[3].X + tallerRow.Tags[3].Width);
    }

    [TestMethod]
    public void Overflow_UsesTheLastAvailableRowAndHandlesNarrowOrEmptyLayouts()
    {
        var tags = Enumerable.Repeat(new HistoryTagSize(70, 30), 8).ToArray();
        var layout = HistoryTagOverflowLayout.Calculate(tags, new(40, 30), 160, 65);
        Assert.HasCount(3, layout.Tags);
        Assert.AreEqual(new HistoryTagPlacement(120, 30, 40, 30), layout.More);
        var narrow = HistoryTagOverflowLayout.Calculate(tags, new(40, 30), 25, 30);
        Assert.IsEmpty(narrow.Tags);
        Assert.AreEqual(new HistoryTagPlacement(0, 0, 25, 30), narrow.More);
        var empty = HistoryTagOverflowLayout.Calculate([], new(40, 30), 160, 40);
        Assert.IsEmpty(empty.Tags);
        Assert.IsNull(empty.More);
    }
}
