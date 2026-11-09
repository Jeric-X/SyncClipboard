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
