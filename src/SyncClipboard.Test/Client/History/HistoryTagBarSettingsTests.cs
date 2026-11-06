using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryTagBarSettingsTests
{
    [TestMethod]
    public void PinningPreviewsOrderWithoutChangingSavedConfiguration()
    {
        string[] saved = ["b", "empty"];
        var model = new HistoryTagBarSettingsViewModel(new Dictionary<string, int> { ["a"] = 2, ["b"] = 1, ["c"] = 3 }, saved);
        var a = model.Tags.Single(item => item.Name == "a");
        var b = model.Tags.Single(item => item.Name == "b");
        a.IsPinned = true;
        AssertNames(model.Tags.Select(item => item.Name), "b", "empty", "a", "c");
        Assert.IsTrue(a.IsPinned);
        b.IsPinned = false;
        AssertNames(model.Tags.Select(item => item.Name), "empty", "a", "b", "c");
        Assert.IsFalse(b.IsPinned);
        model.Tags[0].IsPinned = false;
        AssertNames(model.PinnedNames, "a");
        AssertNames(saved, "b", "empty");
        Assert.AreSame(a, model.Tags[0]);
        Assert.AreSame(b, model.Tags[1]);
    }

    [TestMethod]
    public void DragPreviewFillsPinnedGapAndKeepsUnpinnedSourceUntilDrop()
    {
        var model = new HistoryTagBarSettingsViewModel(new Dictionary<string, int> { ["free"] = 1 }, ["a", "b", "c"]);
        var a = model.PinnedTags[0];
        var drag = new HistoryTagBarDragPreview(model, a);
        AssertNames(drag.PinnedTags.Select(item => item.Name), "b", "c");
        AssertNames(model.PinnedNames, "a", "b", "c");
        drag.PreviewPinned(2);
        AssertNames(drag.PinnedTags.Select(item => item.Name), "b", "c", "a");
        drag.Commit();
        AssertNames(model.PinnedNames, "b", "c", "a");

        var free = model.UnpinnedTags[0];
        drag = new(model, free);
        drag.PreviewPinned(1);
        Assert.AreSame(free, drag.UnpinnedTags[0]);
        Assert.IsFalse(free.IsPinned);
        drag.Commit();
        AssertNames(model.PinnedNames, "b", "free", "c", "a");
        Assert.IsEmpty(model.UnpinnedTags);
        Assert.IsTrue(free.IsPinned);
        drag = new(model, free);
        drag.PreviewUnpinned();
        drag.Commit();
        AssertNames(model.PinnedNames, "b", "c", "a");
        Assert.AreSame(free, model.UnpinnedTags[0]);
        Assert.IsFalse(free.IsPinned);
    }

    [TestMethod]
    public void CancellingDragAndRestoringOrderDoNotLeaveUnsavedChanges()
    {
        var model = new HistoryTagBarSettingsViewModel(new Dictionary<string, int> { ["a"] = 1, ["b"] = 1 }, ["a", "b"]);
        var a = model.PinnedTags[0];
        var drag = new HistoryTagBarDragPreview(model, a);
        drag.PreviewPinned(1);
        drag.ClearTarget();
        drag.Commit();
        AssertNames(model.PinnedNames, "a", "b");
        Assert.IsFalse(model.HasChanges);
        model.Move(a, true, 2);
        Assert.IsTrue(model.HasChanges);
        model.Move(a, true, 0);
        Assert.IsFalse(model.HasChanges);
        a.IsPinned = false;
        Assert.IsTrue(model.HasChanges);
        model.Move(a, true, 0);
        Assert.IsFalse(model.HasChanges);
        model.Move(a, true, 2);
        model.AcceptChanges();
        Assert.IsFalse(model.HasChanges);
    }

    [TestMethod]
    public void DragIntoEmptyPinnedGroupAndReturnToUnpinnedSortsByName()
    {
        var model = new HistoryTagBarSettingsViewModel(new Dictionary<string, int> { ["a"] = 1, ["b"] = 1 }, []);
        var b = model.UnpinnedTags[1];
        var drag = new HistoryTagBarDragPreview(model, b);
        drag.PreviewPinned(0);
        AssertNames(drag.UnpinnedTags.Select(item => item.Name), "a", "b");
        Assert.IsFalse(model.HasChanges);
        drag.Commit();
        AssertNames(model.PinnedNames, "b");
        drag = new(model, b);
        drag.PreviewUnpinned();
        drag.Commit();
        Assert.IsEmpty(model.PinnedTags);
        AssertNames(model.UnpinnedTags.Select(item => item.Name), "a", "b");
        Assert.IsFalse(model.HasChanges);
    }

    [TestMethod]
    public void LastUnusedPinCanBeRemovedAndCountPreferenceIsRespected()
    {
        var model = new HistoryTagBarSettingsViewModel(new Dictionary<string, int>(), ["empty"], true);
        Assert.AreEqual("empty (0)", model.Tags[0].Text);
        model.Tags[0].IsPinned = false;
        Assert.IsTrue(model.IsEmpty);
        Assert.IsEmpty(model.PinnedNames);
    }

    private static void AssertNames(IEnumerable<string> actual, params string[] expected) =>
        CollectionAssert.AreEqual(expected, actual.ToArray());
}
