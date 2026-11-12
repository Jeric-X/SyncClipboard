using SyncClipboard.Core;
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
    public void Order_UnpinningUnusedNameDoesNotChangeSavedPreference()
    {
        string[] saved = ["empty", "used"];
        var counts = new Dictionary<string, int> { ["used"] = 2 };
        var order = new HistoryTagOrderViewModel(counts, saved);
        order.Move(order.Pinned[0], false, 0);
        AssertNames(order.PinnedNames, "used");
        AssertNames(saved, "empty", "used");
        var reopened = new HistoryTagOrderViewModel(counts, saved);
        CollectionAssert.AreEqual(saved, reopened.PinnedNames);
    }

    [TestMethod]
    public async Task RemovedLastAssignment_LeavesPinnedTagsSelectableInSavedOrder()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        fixture.InitializeTags();
        await fixture.LoadAsync();
        var key = fixture.Records[0].Key;
        await fixture.ChangeStoredTagsAsync(0, ["Z-pin", "A-pin"]);
        await fixture.ChangeStoredTagsAsync(1, ["known"]);
        await fixture.ViewModel.SetPinnedTagsAsync(["Z-pin", "A-pin"]);
        await fixture.Manager.EditTagsAsync([key], [], ["Z-pin", "A-pin"], token);
        await fixture.ViewModel.RefreshTagOptionsAsync();
        var counts = await fixture.Manager.GetTagCountsAsync(token);
        AssertNames(counts.Keys, "known");

        var pinned = new ConfigBase(fixture.RuntimeConfig.Path).GetConfig<HistoryWindowConfig>().PinnedTags;
        var settings = new HistoryTagBarSettingsViewModel(counts, pinned, true);
        AssertNames(settings.PinnedNames, "Z-pin", "A-pin");
        Assert.AreEqual("Z-pin (0)", settings.PinnedTags[0].Text);
        var editor = new HistoryTagEditorViewModel(counts.Keys, [[]], pinned);
        AssertNames(editor.Choices.Select(tag => tag.Name), "Z-pin", "A-pin", "known");
        editor.Choices.Single(tag => tag.Name == "A-pin").IsChecked = true;
        await fixture.Manager.EditTagsAsync([key], editor.Added, editor.Removed, token);
        AssertNames((await fixture.Manager.GetSyncSnapshotAsync(key, token))!.Tags, "A-pin");

        var picker = new HistoryTagPickerViewModel(counts.Keys, ["A-pin"], async (name, add) =>
        {
            await fixture.Manager.EditTagsAsync([key], add ? [name] : [], add ? [] : [name], token);
        }, pinned);
        await picker.ToggleAsync("Z-pin");
        AssertNames((await fixture.Manager.GetSyncSnapshotAsync(key, token))!.Tags, "A-pin", "Z-pin");
        await picker.ToggleAsync("Z-pin");
        await picker.ToggleAsync("0-new");
        AssertNames(picker.Suggestions.Select(tag => tag.Name), "Z-pin", "A-pin", "0-new", "known");
        picker.Search = "pin";
        AssertNames(picker.Suggestions.Select(tag => tag.Name), "pin", "Z-pin", "A-pin");
        AssertNames(new ConfigBase(fixture.RuntimeConfig.Path).GetConfig<HistoryWindowConfig>().PinnedTags, "Z-pin", "A-pin");
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
    public async Task SavingOrder_WriteFailurePreservesSavedPinsAndAllowsRetry()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        Fixture.SetField(AppCore.TryGetCurrent()!, "<Logger>k__BackingField", fixture.Logger.Object);
        fixture.InitializeTags();
        await fixture.ViewModel.SetPinnedTagsAsync(["saved"]);
        var draft = new HistoryTagBarSettingsViewModel(new Dictionary<string, int> { ["new"] = 1 }, ["saved"]);
        draft.UnpinnedTags[0].IsPinned = true;
        var path = fixture.RuntimeConfig.Path;
        var backup = path + ".backup";
        File.Move(path, backup);
        Directory.CreateDirectory(path);
        try
        {
            await Assert.ThrowsExactlyAsync<IOException>(() => fixture.ViewModel.SetPinnedTagsAsync(draft.PinnedNames));
            AssertNames(fixture.ViewModel.PinnedTags, "saved");
            AssertNames(fixture.ViewModel.TagOptions.Select(item => item.Name), null, "saved");
            Assert.IsTrue(draft.HasChanges);
            AssertNames(draft.PinnedNames, "saved", "new");
        }
        finally
        {
            Directory.Delete(path);
            File.Move(backup, path);
        }
        await fixture.ViewModel.SetPinnedTagsAsync(draft.PinnedNames);
        AssertNames(new ConfigBase(path).GetConfig<HistoryWindowConfig>().PinnedTags, "saved", "new");
        AssertNames(fixture.ViewModel.TagOptions.Select(item => item.Name), null, "saved", "new");
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
