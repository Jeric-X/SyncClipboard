using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels.Sub;
using System.Globalization;
using Fixture = SyncClipboard.Test.Client.History.HistoryViewModelPagingTests.Fixture;

namespace SyncClipboard.Test.Client.History;

[TestClass]
[DoNotParallelize]
public class HistoryTagShortcutTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Navigation_FollowsDisplayedOrderWrapsAndFiltersRecords()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.InitializeTags();
        await fixture.ViewModel.RefreshTagOptionsAsync();
        await fixture.ViewModel.SetPinnedTagsAsync(["Red"]);
        var model = fixture.ViewModel;
        var visible = model.TagOptions.Take(3).ToArray(); // All, Red, Blue; red is clipped.

        Assert.AreEqual("Red", model.SelectAdjacentTag(visible, true)!.Name);
        await fixture.ReloadAsync();
        Assert.HasCount(20, fixture.Records);
        Assert.IsTrue(fixture.Records.All(record => record.Tags.Contains("Red")));
        Assert.AreEqual("Blue", model.SelectAdjacentTag(visible, true)!.Name);
        Assert.IsNull(model.SelectAdjacentTag(visible, true)!.Name);
        Assert.IsEmpty(model.SelectedTags);
        Assert.AreEqual("Blue", model.SelectAdjacentTag(visible, false)!.Name);
        Assert.AreEqual("Red", model.SelectAdjacentTag(visible, false)!.Name);
        Assert.IsNull(model.SelectAdjacentTag(visible, false)!.Name);

        Assert.AreEqual("red", model.SelectAdjacentTag(model.TagOptions, false)!.Name);
        // Collapsing while a clipped tag is selected enters the visible range at its boundary.
        Assert.IsNull(model.SelectAdjacentTag(visible, true)!.Name);
        model.SelectedTags = ["red"];
        Assert.AreEqual("Blue", model.SelectAdjacentTag(visible, false)!.Name);
        await fixture.ReloadAsync();
        fixture.Logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Navigation_ReplacesMultiSelectionAndHandlesEmptyOrHiddenBar()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.InitializeTags();
        await fixture.ViewModel.RefreshTagOptionsAsync();
        var model = fixture.ViewModel;
        model.MultiTagFilter = true;
        model.SelectedTags = ["red", "Blue"];
        Assert.AreEqual("Red", model.SelectAdjacentTag(model.TagOptions, true)!.Name);
        CollectionAssert.AreEqual(new[] { "Red" }, model.SelectedTags);
        Assert.IsTrue(model.MultiTagFilter);
        Assert.IsNull(model.SelectAdjacentTag([], true));
        CollectionAssert.AreEqual(new[] { "Red" }, model.SelectedTags);
        Assert.IsNull(model.SelectAdjacentTag([model.TagOptions[0]], false)!.Name);
        Assert.IsEmpty(model.SelectedTags);

        model.ShowTagBar = false;
        Assert.IsNull(model.SelectAdjacentTag(model.TagOptions, true));
        Assert.IsEmpty(model.SelectedTags);
        await fixture.ReloadAsync();
    }

    [TestMethod]
    public async Task ConfiguredKeys_DispatchToWindowAndRespectVisibilityAndModalState()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        using var services = new ConfigurationTestServices();
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{}");
            var config = new ConfigManager(path, services.Upgrader);
            var window = new Mock<IWindow>();
            var model = fixture.ViewModel;
            Fixture.SetField(model, "_configManager", config);
            Fixture.SetField(model, "window", window.Object);
            Assert.IsTrue(model.HandleKeyPress(Key.Left, isCtrlPressed: true));
            Assert.IsTrue(model.HandleKeyPress(Key.Right, isCtrlPressed: true));
            window.Verify(value => value.NavigateTagFilter(false), Times.Once);
            window.Verify(value => value.NavigateTagFilter(true), Times.Once);
            window.Invocations.Clear();
            config.SetConfig(new HistoryShortcutConfig
            {
                Shortcuts = { [HistoryShortcutAction.NextTag] = new(Key.F6) }
            });
            Assert.IsFalse(model.HandleKeyPress(Key.Right, isCtrlPressed: true));
            Assert.IsTrue(model.HandleKeyPress(Key.F6));
            window.Verify(value => value.NavigateTagFilter(true), Times.Once);
            window.Invocations.Clear();

            fixture.RuntimeConfig.SetConfig(new HistoryWindowConfig { ShowTagBar = false });
            Assert.IsFalse(model.HandleKeyPress(Key.F6));
            fixture.RuntimeConfig.SetConfig(new HistoryWindowConfig());
            Fixture.SetField(model, "selectedFilter", HistoryFilterType.Transferring);
            Assert.IsFalse(model.HandleKeyPress(Key.F6));
            Fixture.SetField(model, "selectedFilter", HistoryFilterType.All);
            Fixture.SetField(model, "_modalOperations", 1);
            Assert.IsFalse(model.HandleKeyPress(Key.F6));
            window.VerifyNoOtherCalls();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("en")]
    [DataRow("zh-CN")]
    public void Instructions_IncludeConfiguredTagNavigation(string culture)
    {
        var previous = Strings.Culture;
        try
        {
            Strings.Culture = CultureInfo.GetCultureInfo(culture);
            var config = new HistoryShortcutConfig
            {
                Shortcuts = { [HistoryShortcutAction.NextTag] = new(Key.F6) }
            };
            var instructions = HistoryShortcutDescriptions.BuildInstructions(config);
            Assert.Contains(HistoryShortcutDescriptions.GetActionName(HistoryShortcutAction.PreviousTag), instructions);
            Assert.Contains(string.Format(Strings.HistoryWindowOperationFormat, "F6",
                HistoryShortcutDescriptions.GetActionName(HistoryShortcutAction.NextTag)), instructions);
        }
        finally
        {
            Strings.Culture = previous;
        }
    }
}
