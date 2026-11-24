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
public class HistoryTitleShortcutTests
{
    public TestContext TestContext { get; set; } = null!;

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
            var window = new Mock<IHistoryWindow>();
            var model = fixture.ViewModel;
            Fixture.SetField(model, "_configManager", config);
            Fixture.SetField(model, "window", window.Object);
            model.SelectedItem = fixture.Records[0];
            Assert.IsTrue(model.HandleKeyPress(Key.F2));
            window.Verify(value => value.ShowTitleEditor(), Times.Once);
            window.Invocations.Clear();

            using (model.HoldForModalOperation())
            {
                var selected = model.SelectedItem;
                Assert.IsFalse(model.HandleKeyPress(Key.F2));
                Assert.IsFalse(model.HandleKeyPress(Key.Down));
                Assert.IsFalse(model.HandleKeyPress(Key.Enter));
                Assert.IsFalse(model.HandleKeyPress(Key.Esc));
                Assert.AreSame(selected, model.SelectedItem);
            }
            model.IsMultiSelecting = true;
            model.HandleKeyPress(Key.F2);
            model.IsMultiSelecting = false;
            Fixture.SetField(model, "selectedFilter", HistoryFilterType.Transferring);
            model.HandleKeyPress(Key.F2);
            Fixture.SetField(model, "selectedFilter", HistoryFilterType.All);
            model.SelectedItem = null;
            model.HandleKeyPress(Key.F2);
            window.VerifyNoOtherCalls();

            config.SetConfig(new HistoryShortcutConfig { Shortcuts = { [HistoryShortcutAction.EditTitle] = new(Key.F3) } });
            model.SelectedItem = fixture.Records[0];
            Assert.IsFalse(model.HandleKeyPress(Key.F2));
            Assert.IsTrue(model.HandleKeyPress(Key.F3));
            window.Verify(value => value.ShowTitleEditor(), Times.Once);
            window.Invocations.Clear();

            config.SetConfig(new HistoryShortcutConfig { Shortcuts = { [HistoryShortcutAction.EditTitle] = Hotkey.Nothing } });
            Assert.IsFalse(model.HandleKeyPress(Key.F2));
            Assert.IsFalse(model.HandleKeyPress(Key.F3));
            window.VerifyNoOtherCalls();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
