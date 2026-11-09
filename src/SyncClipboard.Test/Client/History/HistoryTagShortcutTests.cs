using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels.Sub;
using Fixture = SyncClipboard.Test.Client.History.HistoryViewModelPagingTests.Fixture;

namespace SyncClipboard.Test.Client.History;

[TestClass]
[DoNotParallelize]
public class HistoryTagShortcutTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ClippedSelection_StaysWithinVisibleCandidates()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.InitializeTags();
        await fixture.ViewModel.RefreshTagOptionsAsync();
        await fixture.ViewModel.SetPinnedTagsAsync(["Red"]);
        var model = fixture.ViewModel;
        var visible = model.TagOptions.Take(3).ToArray(); // All, Red, Blue; red is clipped.

        model.SelectedTags = ["red"];
        // If resizing clips the selected tag, navigation enters the visible range at its boundary.
        Assert.IsNull(model.SelectAdjacentTag(visible, true)!.Name);
        model.SelectedTags = ["red"];
        Assert.AreEqual("Blue", model.SelectAdjacentTag(visible, false)!.Name);
        await fixture.ReloadAsync();
        fixture.Logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task EmptyCandidates_PreserveCurrentFilter()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.InitializeTags();
        await fixture.ViewModel.RefreshTagOptionsAsync();
        var model = fixture.ViewModel;
        model.SelectedTags = ["Red"];
        Assert.IsNull(model.SelectAdjacentTag([], true));
        Assert.AreEqual("Red", model.SelectedTags.Single());
        await fixture.ReloadAsync();
    }

    [TestMethod]
    public async Task HiddenBarAndModalOperation_DoNotConsumeTagShortcuts()
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
            Assert.IsTrue(model.HandleKeyPress(Key.Right, isCtrlPressed: true));
            window.Verify(value => value.NavigateTagFilter(true), Times.Once);
            window.Invocations.Clear();

            fixture.RuntimeConfig.SetConfig(new HistoryWindowConfig { ShowTagBar = false });
            Assert.IsFalse(model.HandleKeyPress(Key.Right, isCtrlPressed: true));
            fixture.RuntimeConfig.SetConfig(new HistoryWindowConfig());
            Fixture.SetField(model, "selectedFilter", HistoryFilterType.Transferring);
            Assert.IsFalse(model.HandleKeyPress(Key.Right, isCtrlPressed: true));
            Fixture.SetField(model, "selectedFilter", HistoryFilterType.All);
            using var modal = model.HoldForModalOperation();
            Assert.IsFalse(model.HandleKeyPress(Key.Right, isCtrlPressed: true));
            window.VerifyNoOtherCalls();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
