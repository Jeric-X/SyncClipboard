using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryTagEditorTests
{
    private static readonly string[] ExpectedAddedTags = ["工作"];
    private static readonly string[] ExpectedRemovedTags = ["Prompt"];

    [TestMethod]
    public void Editor_PreservesUntouchedMixedTagsAndOnlyAppliesExplicitChanges()
    {
        var editor = new HistoryTagEditorViewModel(["代码"], [["Prompt", "工作"], ["Prompt"]]);
        Assert.IsNull(editor.Choices.Single(t => t.Name == "工作").IsChecked);
        Assert.IsEmpty(editor.Added);
        Assert.IsEmpty(editor.Removed);
        editor.Choices.Single(t => t.Name == "工作").IsChecked = true;
        editor.Choices.Single(t => t.Name == "Prompt").IsChecked = false;
        CollectionAssert.AreEqual(ExpectedAddedTags, editor.Added);
        CollectionAssert.AreEqual(ExpectedRemovedTags, editor.Removed);
        editor.Search = " 新标签 ";
        editor.AddCommand.Execute(null);
        Assert.Contains("新标签", editor.Added);
    }
}
