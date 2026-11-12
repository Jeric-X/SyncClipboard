using SyncClipboard.Core.ViewModels;
using Fixture = SyncClipboard.Test.Client.History.HistoryViewModelPagingTests.Fixture;

namespace SyncClipboard.Test.Client.History;

[TestClass]
[DoNotParallelize]
public class HistoryTagContextMenuTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RenameAndDelete_UpdateAllKnownRecordsPinsAndFilters()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.InitializeTags();
        var model = fixture.ViewModel;
        await model.RefreshTagOptionsAsync();
        await model.SetPinnedTagsAsync(["Red", "empty"]);
        model.SelectedTags = ["Red"];
        await fixture.ReloadAsync();

        Assert.IsTrue(await model.RenameTagAsync("Red", "Renamed", false));
        await fixture.ReloadAsync();
        AssertNames(model.PinnedTags, "Renamed", "empty");
        AssertNames(model.SelectedTags, "Renamed");
        Assert.AreEqual(50, model.TagOptions.Single(tag => tag.Name == "Renamed").Count);
        Assert.IsFalse(model.TagOptions.Any(tag => tag.Name == "Red"));

        await model.DeleteTagAsync("Renamed");
        await fixture.ReloadAsync();
        AssertNames(model.PinnedTags, "empty");
        Assert.IsEmpty(model.SelectedTags);
        Assert.IsTrue(model.TagOptions.Single(tag => tag.Name is null).IsSelected);
        Assert.IsFalse((await fixture.Manager.GetTagCountsAsync(TestContext.CancellationTokenSource.Token)).ContainsKey("Renamed"));
        Assert.IsFalse(model.TagOptions.Any(tag => tag.Name == "Renamed"));
        Assert.IsNotEmpty(fixture.Records);
    }

    [TestMethod]
    public async Task MergeIntoEmptyPin_RequiresConfirmationAndPreservesDestinationPosition()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        fixture.InitializeTags();
        var model = fixture.ViewModel;
        await model.SetPinnedTagsAsync(["source", "middle", "target"]);
        model.MultiTagFilter = true;
        model.SelectedTags = ["source", "target"];
        await fixture.ReloadAsync();
        Assert.IsFalse(await model.RenameTagAsync("source", "target", false));
        AssertNames(model.PinnedTags, "source", "middle", "target");
        Assert.IsTrue(await model.RenameTagAsync("source", "target", true));
        await fixture.ReloadAsync();
        AssertNames(model.PinnedTags, "middle", "target");
        AssertNames(model.SelectedTags, "target");
        await model.DeleteTagAsync("target");
        await fixture.ReloadAsync();
        AssertNames(model.PinnedTags, "middle");
        Assert.IsEmpty(model.SelectedTags);
        Assert.IsFalse(model.TagOptions.Any(tag => tag.Name == "source" || tag.Name == "target"));
    }

    [TestMethod]
    public async Task RenameDialog_RequiresSeparateConfirmationForEachConflictingName()
    {
        var writes = new List<string>();
        var model = new HistoryTagRenameViewModel("old", (name, merge) =>
        {
            if (merge)
                writes.Add(name);
            return Task.FromResult(merge);
        })
        {
            NewName = "existing"
        };
        Assert.IsFalse(await model.SaveAsync());
        Assert.IsNotEmpty(model.Message);
        Assert.IsEmpty(writes);
        model.NewName = "another";
        Assert.IsEmpty(model.Message);
        Assert.IsFalse(await model.SaveAsync());
        Assert.IsEmpty(writes);
        Assert.IsTrue(await model.SaveAsync());
        AssertNames(writes, "another");
    }

    [TestMethod]
    public async Task RenameDialog_InvalidInputAndSaveFailureKeepEditorOpenForRetry()
    {
        var attempts = 0;
        var model = new HistoryTagRenameViewModel("old", (_, _) =>
        {
            attempts++;
            return attempts == 1 ? throw new IOException("save failed") : Task.FromResult(true);
        })
        {
            NewName = "\n"
        };
        Assert.IsFalse(await model.SaveAsync());
        Assert.AreEqual(0, attempts);
        model.NewName = "new";
        Assert.IsFalse(await model.SaveAsync());
        Assert.AreEqual("save failed", model.Message);
        Assert.IsTrue(await model.SaveAsync());
        Assert.AreEqual(2, attempts);
    }
    private static void AssertNames(IEnumerable<string> actual, params string[] expected) =>
        CollectionAssert.AreEqual(expected, actual.ToArray());

}
