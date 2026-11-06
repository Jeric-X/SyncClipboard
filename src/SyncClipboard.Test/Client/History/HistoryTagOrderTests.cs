using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;
using System.Text.Json.Nodes;
using Fixture = SyncClipboard.Test.Client.History.HistoryViewModelPagingTests.Fixture;

namespace SyncClipboard.Test.Client.History;

[TestClass]
[DoNotParallelize]
public class HistoryTagOrderTests
{
    public TestContext TestContext { get; set; } = null!;

    private static void AssertNames(IEnumerable<string?> actual, params string?[] expected) =>
        CollectionAssert.AreEqual(expected, actual.ToArray());

    [TestMethod]
    public void Order_PreservesPinnedPreferenceAndSortsOthersOrdinally()
    {
        var order = new HistoryTagOrderViewModel(new Dictionary<string, int>
        {
            ["B"] = 2,
            ["a"] = 1,
            ["A"] = 3,
            ["b"] = 4
        }, ["empty", "B", "empty"]);
        AssertNames(order.PinnedNames, "empty", "B");
        AssertNames(order.Unpinned.Select(item => item.Name), "A", "a", "b");
        Assert.AreEqual(0, order.Pinned[0].Count);
        Assert.AreEqual(2, order.Pinned[1].Count);
    }

    [TestMethod]
    public void Order_MovesAcrossColumnsAndUsesInsertionBoundariesForReordering()
    {
        var order = new HistoryTagOrderViewModel(new Dictionary<string, int>
        {
            ["A"] = 1,
            ["B"] = 2,
            ["C"] = 3,
            ["D"] = 4
        }, ["A", "B"]);
        var a = order.Pinned[0];
        var c = order.Unpinned[0];
        order.Move(a, true, 2);
        AssertNames(order.PinnedNames, "B", "A");
        order.Move(c, true, 1);
        AssertNames(order.PinnedNames, "B", "C", "A");
        order.Move(a, true, 0);
        AssertNames(order.PinnedNames, "A", "B", "C");
        order.Move(c, false, 0);
        AssertNames(order.Unpinned.Select(item => item.Name), "C", "D");
        order.Move(c, false, 2);
        AssertNames(order.Unpinned.Select(item => item.Name), "C", "D");
        Assert.AreSame(c, order.Unpinned[0]);
    }

    [TestMethod]
    public void Order_UnpinningUnusedNameDropsItAndDraftDoesNotChangeSavedPreference()
    {
        string[] saved = ["empty", "used"];
        var counts = new Dictionary<string, int> { ["used"] = 2 };
        var order = new HistoryTagOrderViewModel(counts, saved);
        order.Move(order.Pinned[0], false, 0);
        AssertNames(order.PinnedNames, "used");
        Assert.IsEmpty(order.Unpinned);
        AssertNames(saved, "empty", "used");
        var reopened = new HistoryTagOrderViewModel(counts, saved);
        CollectionAssert.AreEqual(saved, reopened.PinnedNames);
    }

    [TestMethod]
    public async Task SavingOrder_PersistsNormalizedNamesAndMovesExistingFiltersWithoutLosingSelection()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.InitializeTags();
        await fixture.ViewModel.RefreshTagOptionsAsync();
        var options = fixture.ViewModel.TagOptions;
        var red = options.Single(item => item.Name == "Red");
        var blue = options.Single(item => item.Name == "Blue");
        red.SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        await fixture.ViewModel.SetPinnedTagsAsync(["Red", " empty ", "Red", "Cafe\u0301"]);
        AssertNames(options.Select(item => item.Name), null, "Red", "empty", "Café", "Blue", "red");
        Assert.AreSame(red, options[1]);
        Assert.AreSame(blue, options[4]);
        Assert.IsTrue(red.IsSelected);
        Assert.AreEqual(0, options[2].Count);
        var reloaded = new ConfigBase(fixture.RuntimeConfig.Path).GetConfig<HistoryWindowConfig>();
        AssertNames(reloaded.PinnedTags, "Red", "empty", "Café");
        await fixture.ViewModel.SetPinnedTagsAsync([]);
        AssertNames(options.Select(item => item.Name), null, "Blue", "Red", "red");
        Assert.AreSame(red, options[2]);
        Assert.IsTrue(red.IsSelected);
        fixture.Logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task InvalidOrder_DoesNotOverwriteSavedPreference()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        fixture.InitializeTags();
        await fixture.ViewModel.SetPinnedTagsAsync(["saved"]);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.ViewModel.SetPinnedTagsAsync(["new", "bad\nname"]));
        AssertNames(fixture.ViewModel.PinnedTags, "saved");
        var reloaded = new ConfigBase(fixture.RuntimeConfig.Path).GetConfig<HistoryWindowConfig>();
        AssertNames(reloaded.PinnedTags, "saved");
        fixture.Logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task SelectedEmptyTag_RemainsAfterUnpinningAndLegacyUnpinnedOrderIsIgnored()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        fixture.RuntimeConfig.SetNode(HistoryWindowConfig.ConfigKey,
            JsonNode.Parse("""{"PinnedTags":["empty"],"UnpinnedTags":["Red","Blue"]}"""));
        await fixture.AddTagsAsync();
        fixture.InitializeTags();
        await fixture.ViewModel.RefreshTagOptionsAsync();
        var empty = fixture.ViewModel.TagOptions.Single(item => item.Name == "empty");
        empty.SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        await fixture.ViewModel.SetPinnedTagsAsync([]);
        AssertNames(fixture.ViewModel.TagOptions.Select(item => item.Name), null, "Blue", "Red", "empty", "red");
        Assert.AreSame(empty, fixture.ViewModel.TagOptions[3]);
        Assert.IsTrue(empty.IsSelected);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.RuntimeConfig.Path, TestContext.CancellationTokenSource.Token));
        Assert.IsNull(saved![HistoryWindowConfig.ConfigKey]!["UnpinnedTags"]);
        fixture.ViewModel.TagOptions[0].SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        await fixture.ViewModel.RefreshTagOptionsAsync();
        Assert.IsFalse(fixture.ViewModel.TagOptions.Any(item => item.Name == "empty"));
        fixture.Logger.VerifyNoOtherCalls();
    }
}
