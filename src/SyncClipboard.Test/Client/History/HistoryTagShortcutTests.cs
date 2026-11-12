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
    [DataRow(false, true, false, true)]
    [DataRow(true, true, false, false)]
    [DataRow(false, false, false, false)]
    [DataRow(false, true, true, false)]
    public async Task ClosingPicker_ReappliesFocusLossPolicy(bool active, bool closeOnBlur, bool topmost, bool shouldHide)
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        var window = new Mock<IWindow>();
        window.SetupGet(value => value.IsActive).Returns(active);
        window.SetupGet(value => value.IsVisible).Returns(true);
        var model = fixture.ViewModel;
        Fixture.SetField(model, "window", window.Object);
        model.CloseWhenLostFocus = closeOnBlur;
        model.IsTopmost = topmost;
        using (model.HoldForTagPicker())
        {
            model.OnLostFocus();
            window.Verify(value => value.Hide(), Times.Never);
            Assert.IsFalse(model.HandleKeyPress(Key.Enter));
        }
        window.Verify(value => value.Hide(), shouldHide ? Times.Once : Times.Never);
    }

    [TestMethod]
    public async Task EditShortcut_UsesConfiguredKeyAndOnlyOpensForASingleCurrentRecord()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.LoadAsync();
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
            model.SelectedItem = fixture.Records[0];
            Assert.IsTrue(model.HandleKeyPress(Key.T, isCtrlPressed: true));
            window.Verify(value => value.ShowTagPicker(), Times.Once);
            window.Invocations.Clear();

            using (model.HoldForModalOperation())
            {
                var selected = model.SelectedItem;
                Assert.IsFalse(model.HandleKeyPress(Key.T, isCtrlPressed: true));
                Assert.IsFalse(model.HandleKeyPress(Key.Down));
                Assert.IsFalse(model.HandleKeyPress(Key.Enter));
                Assert.IsFalse(model.HandleKeyPress(Key.Esc));
                Assert.AreSame(selected, model.SelectedItem);
            }
            model.IsMultiSelecting = true;
            model.HandleKeyPress(Key.T, isCtrlPressed: true);
            model.IsMultiSelecting = false;
            Fixture.SetField(model, "selectedFilter", HistoryFilterType.Transferring);
            model.HandleKeyPress(Key.T, isCtrlPressed: true);
            Fixture.SetField(model, "selectedFilter", HistoryFilterType.All);
            model.SelectedItem = null;
            model.HandleKeyPress(Key.T, isCtrlPressed: true);
            window.VerifyNoOtherCalls();

            config.SetConfig(new HistoryShortcutConfig { Shortcuts = { [HistoryShortcutAction.EditTags] = new(Key.F2) } });
            model.SelectedItem = fixture.Records[0];
            Assert.IsFalse(model.HandleKeyPress(Key.T, isCtrlPressed: true));
            Assert.IsTrue(model.HandleKeyPress(Key.F2));
            window.Verify(value => value.ShowTagPicker(), Times.Once);
        }
        finally
        {
            File.Delete(path);
        }
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
