using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpHook.Data;
using SharpHook.Simulation;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
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
    private readonly Mock<IMainWindowDialog> dialog = new(MockBehavior.Strict);
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
    public async Task NumberedTargetsStayFixedUntilThePrefixIsReleased()
    {
        var custom = visible[10];
        var editor = Model.CreateRecordShortcutEditor(custom.Key);
        editor.EditingShortcut = new(Key.Meta, Key.Alt, Key.K);
        Assert.IsTrue(await editor.TrySaveAsync(dialog.Object));
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
        var target = visible[1].Text;
        Model.HandleKeyPress(Key.Alt, new Hotkey(Key.Ctrl, Key.Shift, Key.Alt));
        visible = fixture.Records.Skip(6).Take(12).ToArray();
        Model.RefreshQuickPasteHints();
        Assert.IsFalse(oldFirst.HasQuickPasteKeys);
        Model.UpdateQuickPasteModifiers(prefix);
        Assert.AreEqual("3", visible[0].QuickPasteKeys.Single().Label);
        Assert.IsFalse(visible[^1].HasQuickPasteKeys);
        CollectionAssert.AreEqual(expectedLabels, visible[8].QuickPasteKeys.Select(key => key.Label).ToArray());

        Assert.IsTrue(Model.HandleKeyPress(Key.NumPad2, prefix));
        Assert.IsTrue(Model.HandleKeyPress(Key.NumPad2, prefix));
        await Model.ActiveQuickPaste!;
        var calls = simulator.Invocations.ToList();
        var pasteIndex = calls.FindIndex(call => call.Method.Name == nameof(IEventSimulator.SimulateKeyPress) &&
            Equals(call.Arguments[0], KeyCode.VcV));
        foreach (var key in new[] { KeyCode.VcLeftControl, KeyCode.VcRightControl, KeyCode.VcLeftShift, KeyCode.VcRightShift, KeyCode.VcNumPad2 })
        {
            Assert.IsTrue(calls.Take(pasteIndex).Any(call => call.Method.Name == nameof(IEventSimulator.SimulateKeyRelease) &&
                Equals(call.Arguments[0], key)));
        }
        CollectionAssert.AreEqual(new[] { target }, copied);
        simulator.Verify(s => s.SimulateKeyPress(KeyCode.VcV), Times.Once);
        Model.UpdateQuickPasteModifiers(Hotkey.Nothing);
        Assert.IsTrue(visible.All(row => !row.HasQuickPasteKeys));

        Model.HandleKeyPress(Key.Shift, prefix);
        Assert.AreEqual("1", visible[0].QuickPasteKeys.Single().Label);
    }

    [TestMethod]
    public async Task CustomShortcutPastesUnloadedRecordAndCanBeClearedAfterReopening()
    {
        var key = new HistoryRecordKey(ProfileType.Text, 99.ToString("D64"));
        Assert.IsFalse(fixture.Records.Any(record => record.Key == key));
        var editor = Model.CreateRecordShortcutEditor(key);
        editor.EditingShortcut = new(Key.F7);
        Assert.IsTrue(await editor.TrySaveAsync(dialog.Object));
        config = new ConfigManager(config.Path, configServices.Upgrader);
        Fixture.SetField(Model, "_configManager", config);

        using (Model.HoldForModalOperation())
            Assert.IsFalse(Model.HandleKeyPress(Key.F7));
        Assert.IsTrue(Model.HandleKeyPress(Key.F7));
        await Model.ActiveQuickPaste!;
        simulator.Verify(s => s.SimulateKeyRelease(KeyCode.VcF7), Times.Once);
        Assert.AreEqual("record 99", copied.Single());
        simulator.Verify(s => s.SimulateKeyPress(KeyCode.VcV), Times.Once);

        editor = Model.CreateRecordShortcutEditor(key);
        Assert.AreEqual(new Hotkey(Key.F7), editor.EditingShortcut);
        editor.EditingShortcut = Hotkey.Nothing;
        Assert.IsTrue(await editor.TrySaveAsync(dialog.Object));
        Assert.IsFalse(Model.HandleKeyPress(Key.F7));
    }

    [TestMethod]
    public async Task LosingFocusWhileClipboardIsBusyCancelsPaste()
    {
        var shortcut = new Hotkey(Key.F7);
        config.SetConfig(config.GetConfig<HistoryShortcutConfig>().WithRecordShortcut(visible[0].Key, shortcut));
        await LocalClipboard.Semaphore.WaitAsync(TestContext.CancellationTokenSource.Token);
        try
        {
            Assert.IsTrue(Model.HandleKeyPress(Key.F7));
            simulator.Verify(s => s.SimulateKeyRelease(KeyCode.VcF7), Times.Once);
            Model.OnLostFocus();
        }
        finally
        {
            LocalClipboard.Semaphore.Release();
        }
        await Model.ActiveQuickPaste!.WaitAsync(TestContext.CancellationTokenSource.Token);
        simulator.Verify(s => s.SimulateKeyPress(KeyCode.VcV), Times.Never);
    }

    [TestMethod]
    public async Task RejectsConflictsWithFixedActionsAndNumberedShortcuts()
    {
        var editor = Model.CreateRecordShortcutEditor(visible[0].Key);
        editor.EditingShortcut = new(Key.Alt, Key.F8);
        Assert.IsTrue(await editor.TrySaveAsync(dialog.Object));
        var other = Model.CreateRecordShortcutEditor(visible[1].Key);
        foreach (var conflict in new Hotkey[] { new(Key.Ctrl, Key.F), new(Key.Ctrl, Key.Shift, Key._1) })
        {
            other.EditingShortcut = conflict;
            Assert.IsFalse(other.CanSave);
            Assert.IsFalse(await other.TrySaveAsync(dialog.Object));
        }
        var current = config.GetConfig<HistoryShortcutConfig>();
        Assert.IsFalse(current.CanAssign(HistoryShortcutAction.Search, editor.EditingShortcut));
        Assert.IsFalse(current.CanAssign(HistoryShortcutAction.QuickPastePrefix, new(Key.Alt, Key.F)));
        Assert.IsTrue(current.CanAssign(HistoryShortcutAction.QuickPastePrefix, new(Key.Alt, Key.Shift)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReassigningLiveShortcutShowsOwnerPreviewAndOnlyTransfersOnConfirmation(bool confirm)
    {
        var owner = new HistoryRecordKey(ProfileType.Text, 99.ToString("D64"));
        var shortcut = new Hotkey(Key.F8);
        var target = visible[0].Key;
        var before = config.GetConfig<HistoryShortcutConfig>()
            .WithRecordShortcut(owner, shortcut).WithRecordShortcut(target, new(Key.F7));
        config.SetConfig(before);
        var editor = Model.CreateRecordShortcutEditor(target);
        editor.EditingShortcut = shortcut;
        Assert.IsTrue(editor.CanSave);
        var message = string.Format(Strings.HistoryShortcutReassignMessage, "F8", "record 99");
        dialog.Setup(d => d.ShowConfirmationAsync(Strings.HistoryShortcutReassignTitle, message)).ReturnsAsync(confirm);

        Assert.AreEqual(confirm, await editor.TrySaveAsync(dialog.Object));

        dialog.Verify(d => d.ShowConfirmationAsync(Strings.HistoryShortcutReassignTitle, message), Times.Once);
        var saved = config.GetConfig<HistoryShortcutConfig>();
        if (confirm)
            Assert.AreEqual(new HistoryRecordShortcut(target, shortcut), saved.RecordShortcuts.Single());
        else
            Assert.AreEqual(before, saved);
    }

    [TestMethod]
    public async Task ConfirmationForOneRecordDoesNotOverwriteANewOwner()
    {
        var shortcut = new Hotkey(Key.F8);
        var first = visible[1];
        var second = visible[2];
        config.SetConfig(config.GetConfig<HistoryShortcutConfig>().WithRecordShortcut(first.Key, shortcut));
        var editor = Model.CreateRecordShortcutEditor(visible[0].Key);
        editor.EditingShortcut = shortcut;
        var firstMessage = string.Format(Strings.HistoryShortcutReassignMessage, "F8", first.Text);
        var secondMessage = string.Format(Strings.HistoryShortcutReassignMessage, "F8", second.Text);
        dialog.Setup(d => d.ShowConfirmationAsync(Strings.HistoryShortcutReassignTitle, firstMessage))
            .Callback(() => config.SetConfig(config.GetConfig<HistoryShortcutConfig>().WithRecordShortcut(second.Key, shortcut)))
            .ReturnsAsync(true);
        dialog.Setup(d => d.ShowConfirmationAsync(Strings.HistoryShortcutReassignTitle, secondMessage)).ReturnsAsync(false);

        Assert.IsFalse(await editor.TrySaveAsync(dialog.Object));
        Assert.AreEqual(new HistoryRecordShortcut(second.Key, shortcut), config.GetConfig<HistoryShortcutConfig>().RecordShortcuts.Single());
    }

    [TestMethod]
    public async Task DeletingTargetDuringConfirmationPreservesOriginalBinding()
    {
        var target = visible[0];
        var owner = visible[1];
        var shortcut = new Hotkey(Key.F8);
        config.SetConfig(config.GetConfig<HistoryShortcutConfig>().WithRecordShortcut(owner.Key, shortcut));
        var editor = Model.CreateRecordShortcutEditor(target.Key);
        editor.EditingShortcut = shortcut;
        dialog.Setup(d => d.ShowConfirmationAsync(Strings.HistoryShortcutReassignTitle, It.IsAny<string>()))
            .Returns(async () =>
            {
                await fixture.Manager.DeleteHistory(target.ToHistoryRecord(), TestContext.CancellationTokenSource.Token);
                return true;
            });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => editor.TrySaveAsync(dialog.Object));
        Assert.AreEqual(new HistoryRecordShortcut(owner.Key, shortcut), config.GetConfig<HistoryShortcutConfig>().RecordShortcuts.Single());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReclaimingShortcutFromDeletedOrMissingOwnerSkipsConfirmation(bool softDeleted)
    {
        var owner = new HistoryRecordKey(ProfileType.Text, "missing");
        if (softDeleted)
        {
            var deleted = visible[1];
            await fixture.Manager.DeleteHistory(deleted.ToHistoryRecord(), TestContext.CancellationTokenSource.Token);
            owner = deleted.Key;
        }
        var shortcut = new Hotkey(Key.F8);
        config.SetConfig(config.GetConfig<HistoryShortcutConfig>().WithRecordShortcut(owner, shortcut));
        var editor = Model.CreateRecordShortcutEditor(visible[0].Key);
        editor.EditingShortcut = shortcut;

        Assert.IsTrue(await editor.TrySaveAsync(dialog.Object));
        Assert.AreEqual(new HistoryRecordShortcut(visible[0].Key, shortcut),
            config.GetConfig<HistoryShortcutConfig>().RecordShortcuts.Single());
        dialog.VerifyNoOtherCalls();
    }
}
