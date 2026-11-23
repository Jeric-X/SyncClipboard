using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpHook.Data;
using SharpHook.Simulation;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.Permissions;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Shared.Profiles;
using Fixture = SyncClipboard.Test.Client.History.HistoryViewModelPagingTests.Fixture;

namespace SyncClipboard.Test.Client.History;

[TestClass]
[DoNotParallelize]
public class HistoryQuickPasteTests
{
    private Fixture fixture = null!;
    private readonly Mock<IWindow> window = new();
    private HistoryRecordVM[] visible = [];
    private readonly Hotkey prefix = new(Key.Ctrl, Key.Shift);
    private ConfigurationTestServices configServices = null!;
    private ConfigManager config = null!;
    private readonly List<string?> copied = [];
    private readonly Mock<IEventSimulator> simulator = new();
    private ServiceProvider clipboardServices = null!;
    private VirtualKeyboard keyboard = null!;
    private HistoryViewModel Model => fixture.ViewModel;
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.LoadAsync();
        configServices = new ConfigurationTestServices();
        config = new ConfigManager(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), configServices.Upgrader);
        config.SetConfig(new HistoryShortcutConfig { Shortcuts = { [HistoryShortcutAction.QuickPastePrefix] = prefix } });
        window.Setup(w => w.GetVisibleHistoryRecords()).Returns(() => visible);
        visible = fixture.Records.Skip(4).Take(12).ToArray();

        var setter = new Mock<IClipboardSetter<TextProfile>>();
        setter.Setup(s => s.SetLocalClipboard(It.IsAny<ClipboardMetaInfomation>(), It.IsAny<CancellationToken>()))
            .Callback<ClipboardMetaInfomation, CancellationToken>((info, _) => copied.Add(info.Text)).Returns(Task.CompletedTask);
        clipboardServices = new ServiceCollection().AddSingleton(setter.Object).BuildServiceProvider();
        fixture.Dispatcher.Setup(d => d.RunOnMainThreadAsync(It.IsAny<Func<Task>>())).Returns<Func<Task>>(run => run());
        var profileEnv = Mock.Of<IProfileEnv>(env => env.GetPersistentDir() == Path.GetTempPath());
        var permissions = Mock.Of<IPermissionProvider>(p => p.GetState(PermissionCapability.SimulateInput) == PermissionState.Available);
        keyboard = new VirtualKeyboard(permissions, () => simulator.Object);
        Fixture.SetField(Model, "_configManager", config);
        Fixture.SetField(Model, "localClipboardSetter", new LocalClipboardSetter(clipboardServices, fixture.Dispatcher.Object, profileEnv));
        Fixture.SetField(Model, "window", window.Object);
        Fixture.SetField(Model, "keyboard", keyboard);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        keyboard.Dispose();
        await clipboardServices.DisposeAsync();
        File.Delete(config.Path);
        configServices.Dispose();
        await fixture.DisposeAsync();
    }

    [TestMethod]
    public async Task VisibleNumbersFollowScrollAndPasteTheTargetShownWhenPressed()
    {
        var custom = visible[10];
        var editor = Model.CreateRecordShortcutEditor(custom.Key);
        editor.EditingShortcut = new(Key.Meta, Key.Alt, Key.K);
        editor.SaveCommand.Execute(null);
        Model.HandleKeyPress(Key.Shift, prefix);
        CollectionAssert.AreEqual(Enumerable.Range(1, 9).Select(i => i.ToString()).ToArray(),
            visible.Take(9).Select(r => r.QuickPasteKeys.Single().Label).ToArray());
        Assert.IsFalse(visible[9].HasQuickPasteKeys);
        var expectedLabels = OperatingSystem.IsMacOS() ? new[] { "⌥", "⌘", "K" }
            : [OperatingSystem.IsWindows() ? "Win" : "Super", "Alt", "K"];
        CollectionAssert.AreEqual(expectedLabels, custom.QuickPasteKeys.Select(key => key.Label).ToArray());
        Assert.AreEqual(OperatingSystem.IsWindows(), custom.QuickPasteKeys[0].ShowWindowsLogo);
        Assert.AreEqual(OperatingSystem.IsMacOS() ? "Option+Cmd+K" : string.Join("+", expectedLabels), custom.QuickPasteShortcutName);

        var oldFirst = visible[0];
        visible = visible.Skip(2).ToArray();
        Model.RefreshQuickPasteHints();
        Assert.IsFalse(oldFirst.HasQuickPasteKeys);
        Assert.AreEqual("1", visible[0].QuickPasteKeys.Single().Label);
        CollectionAssert.AreEqual(expectedLabels, visible[8].QuickPasteKeys.Select(key => key.Label).ToArray());

        var target = visible[1].Text;
        Assert.IsTrue(Model.HandleKeyPress(Key.NumPad2, prefix));
        Assert.IsTrue(Model.HandleKeyPress(Key.NumPad2, prefix));
        visible = visible.Skip(1).ToArray();
        Model.RefreshQuickPasteHints();
        await Model.HandleKeyReleaseAsync(Key.NumPad2, prefix);
        Assert.IsEmpty(copied);
        await Model.HandleKeyReleaseAsync(Key.Shift, new(Key.Ctrl));
        await Model.HandleKeyReleaseAsync(Key.Ctrl, Hotkey.Nothing);
        CollectionAssert.AreEqual(new[] { target }, copied);
        simulator.Verify(s => s.SimulateKeyPress(KeyCode.VcV), Times.Once);
        Assert.IsTrue(visible.All(row => !row.HasQuickPasteKeys));
    }

    [TestMethod]
    public async Task CustomShortcutPastesUnloadedRecordAndCanBeClearedAfterReopening()
    {
        var key = new HistoryRecordKey(ProfileType.Text, 99.ToString("D64"));
        Assert.IsFalse(fixture.Records.Any(record => record.Key == key));
        var editor = Model.CreateRecordShortcutEditor(key);
        editor.EditingShortcut = new(Key.F7);
        editor.SaveCommand.Execute(null);
        config = new ConfigManager(config.Path, configServices.Upgrader);
        Fixture.SetField(Model, "_configManager", config);

        using (Model.HoldForModalOperation())
            Assert.IsFalse(Model.HandleKeyPress(Key.F7));
        Assert.IsTrue(Model.HandleKeyPress(Key.F7));
        Model.OnLostFocus();
        await Model.HandleKeyReleaseAsync(Key.F7, Hotkey.Nothing);
        Assert.IsEmpty(copied);

        Assert.IsTrue(Model.HandleKeyPress(Key.F7));
        await Model.HandleKeyReleaseAsync(Key.F7, Hotkey.Nothing);
        Assert.AreEqual("record 99", copied.Single());
        simulator.Verify(s => s.SimulateKeyPress(KeyCode.VcV), Times.Once);

        editor = Model.CreateRecordShortcutEditor(key);
        Assert.AreEqual(new Hotkey(Key.F7), editor.EditingShortcut);
        editor.EditingShortcut = Hotkey.Nothing;
        editor.SaveCommand.Execute(null);
        Assert.IsFalse(Model.HandleKeyPress(Key.F7));
    }

    [TestMethod]
    public void RejectsConflictsBetweenRecordsActionsAndNumberedShortcuts()
    {
        var editor = Model.CreateRecordShortcutEditor(visible[0].Key);
        editor.EditingShortcut = new(Key.Alt, Key.F8);
        editor.SaveCommand.Execute(null);
        var other = Model.CreateRecordShortcutEditor(visible[1].Key);
        foreach (var conflict in new Hotkey[] { editor.EditingShortcut, new(Key.Ctrl, Key.F), new(Key.Ctrl, Key.Shift, Key._1) })
        {
            other.EditingShortcut = conflict;
            Assert.IsFalse(other.SaveCommand.CanExecute(null));
        }
        var current = config.GetConfig<HistoryShortcutConfig>();
        Assert.IsFalse(current.CanAssign(HistoryShortcutAction.Search, editor.EditingShortcut));
        Assert.IsFalse(current.CanAssign(HistoryShortcutAction.QuickPastePrefix, new(Key.Alt, Key.F)));
        Assert.IsTrue(current.CanAssign(HistoryShortcutAction.QuickPastePrefix, new(Key.Alt, Key.Shift)));
    }
}
