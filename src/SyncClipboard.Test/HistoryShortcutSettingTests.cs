using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryShortcutSettingTests
{
    private string directory = null!;
    private ConfigManager config = null!;
    private ConfigurationTestServices configurationServices = null!;
    private ServiceProvider services = null!;
    private RemoteClipboardServerFactory factory = null!;
    private HistorySettingViewModel viewModel = null!;
    private Mock<IMainWindowDialog> dialog = null!;

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), $"HistoryShortcutSettings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        configurationServices = new ConfigurationTestServices();
        config = new ConfigManager(Path.Combine(directory, "SyncClipboard.json"), configurationServices.Upgrader);
        services = new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton(Mock.Of<ILogger>())
            .AddSingleton<AccountManager>()
            .BuildServiceProvider();
        factory = new RemoteClipboardServerFactory(services);
        // Clearing history is outside these settings tests, so no history database is required.
        dialog = new Mock<IMainWindowDialog>();
        viewModel = new HistorySettingViewModel(config, null!, dialog.Object, factory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        factory.Dispose();
        services.Dispose();
        configurationServices.Dispose();
        Directory.Delete(directory, true);
    }

    [TestMethod]
    public void SaveShortcut_UpdatesDisplayedBindingAndRuntimeConfiguration()
    {
        var setting = viewModel.ShortcutSettings.Single(row => row.Action == HistoryShortcutAction.Search);
        viewModel.BeginEditShortcut(setting);
        viewModel.EditingShortcut = new Hotkey(Key.F2);
        Assert.IsTrue(viewModel.SaveShortcutCommand.CanExecute(null));
        viewModel.SaveShortcutCommand.Execute(null);
        Assert.AreEqual(new Hotkey(Key.F2), setting.Hotkey);
        Assert.AreEqual(HistoryShortcutAction.Search, config.GetConfig<HistoryShortcutConfig>().Match(new Hotkey(Key.F2)));
        Assert.IsNull(config.GetConfig<HistoryShortcutConfig>().Match(new Hotkey(Key.Ctrl, Key.F)));
    }

    [TestMethod]
    public void SaveShortcut_RejectsReservedAndConflictingBindings()
    {
        var setting = viewModel.ShortcutSettings.Single(row => row.Action == HistoryShortcutAction.Search);
        viewModel.BeginEditShortcut(setting);
        foreach (var hotkey in new[] { new Hotkey(Key.Esc), new Hotkey(Key.Ctrl, Key.W), new Hotkey(Key.Enter) })
        {
            viewModel.EditingShortcut = hotkey;
            Assert.IsTrue(viewModel.ShortcutHasError);
            Assert.IsFalse(viewModel.SaveShortcutCommand.CanExecute(null));
            viewModel.SaveShortcutCommand.Execute(null);
            Assert.AreEqual(new Hotkey(Key.Ctrl, Key.F), setting.Hotkey);
        }
    }

    [TestMethod]
    public void SaveShortcut_RejectsSingleInputCharacterAndAcceptsModifiedCharacter()
    {
        var setting = viewModel.ShortcutSettings.Single(row => row.Action == HistoryShortcutAction.Search);
        viewModel.BeginEditShortcut(setting);
        foreach (var key in new[] { Key.A, Key._1, Key.Semicolon, Key.NumPad5 })
        {
            viewModel.EditingShortcut = new Hotkey(key);
            Assert.IsTrue(viewModel.ShortcutHasError);
            Assert.IsFalse(viewModel.SaveShortcutCommand.CanExecute(null));
            viewModel.SaveShortcutCommand.Execute(null);
            Assert.AreEqual(new Hotkey(Key.Ctrl, Key.F), setting.Hotkey);
        }

        viewModel.EditingShortcut = new Hotkey(Key.Ctrl, Key.A);
        Assert.IsFalse(viewModel.ShortcutHasError);
        Assert.IsTrue(viewModel.SaveShortcutCommand.CanExecute(null));
        viewModel.SaveShortcutCommand.Execute(null);
        Assert.AreEqual(new Hotkey(Key.Ctrl, Key.A), setting.Hotkey);
    }

    [TestMethod]
    public void ValidationMessage_ShowsOnlyCurrentErrorAndClearsForValidInput()
    {
        var setting = viewModel.ShortcutSettings.Single(row => row.Action == HistoryShortcutAction.Search);
        viewModel.BeginEditShortcut(setting);
        var messages = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.ShortcutErrorMessage))
                messages.Add(viewModel.ShortcutErrorMessage);
        };
        var invalidInputs = new (Hotkey Hotkey, string Message)[]
        {
            (new(Key.Ctrl, Key.W), string.Format(Strings.HistoryShortcutReserved, "Ctrl+W")),
            (new(Key.A), string.Format(Strings.HistoryShortcutInputCharacter, "A")),
            (new(Key.Ctrl), Strings.HistoryShortcutModifierOnly),
            (new(Key.Ctrl, Key.C, Key.V), Strings.HistoryShortcutMultipleMainKeys),
            (new(Key.Enter), string.Format(Strings.HistoryShortcutConflict, "Enter", Strings.HistoryShortcutCopyAndPaste))
        };
        foreach (var (hotkey, message) in invalidInputs)
        {
            viewModel.EditingShortcut = hotkey;
            Assert.AreEqual(message, viewModel.ShortcutErrorMessage);
            Assert.IsTrue(viewModel.ShortcutHasError);
            Assert.IsFalse(viewModel.SaveShortcutCommand.CanExecute(null));
        }
        CollectionAssert.AreEqual(invalidInputs.Select(input => input.Message).ToArray(), messages);

        viewModel.EditingShortcut = new Hotkey(Key.Ctrl, Key.A);
        Assert.AreEqual(string.Empty, viewModel.ShortcutErrorMessage);
        Assert.IsFalse(viewModel.ShortcutHasError);
        Assert.IsTrue(viewModel.SaveShortcutCommand.CanExecute(null));

        viewModel.EditingShortcut = new Hotkey(Key.A);
        viewModel.EditingShortcut = Hotkey.Nothing;
        Assert.AreEqual(string.Empty, viewModel.ShortcutErrorMessage);
        Assert.IsTrue(viewModel.SaveShortcutCommand.CanExecute(null));
    }

    [TestMethod]
    public void ValidationMessage_RefreshesConflictingActionWhenConfigurationChanges()
    {
        viewModel.BeginEditShortcut(viewModel.ShortcutSettings.Single(row => row.Action == HistoryShortcutAction.Search));
        viewModel.EditingShortcut = new Hotkey(Key.Enter);
        Assert.AreEqual(string.Format(Strings.HistoryShortcutConflict, "Enter", Strings.HistoryShortcutCopyAndPaste),
            viewModel.ShortcutErrorMessage);

        config.SetConfig(new HistoryShortcutConfig
        {
            Shortcuts =
            {
                [HistoryShortcutAction.CopyAndPaste] = Hotkey.Nothing,
                [HistoryShortcutAction.Copy] = new Hotkey(Key.Enter)
            }
        });

        Assert.AreEqual(string.Format(Strings.HistoryShortcutConflict, "Enter", Strings.HistoryShortcutCopy),
            viewModel.ShortcutErrorMessage);
        Assert.IsTrue(viewModel.ShortcutHasError);
    }

    [TestMethod]
    public async Task RecommendedAction_CanBeAssignedToKeyboardAndBothMouseGestures()
    {
        var setting = viewModel.ShortcutSettings.Single(row => row.Action == HistoryShortcutAction.ExecuteRecommendedAction);
        Assert.AreEqual(Hotkey.Nothing, setting.Hotkey);
        viewModel.BeginEditShortcut(setting);
        viewModel.EditingShortcut = new Hotkey(Key.F3);
        viewModel.SaveShortcutCommand.Execute(null);
        var mouseAction = viewModel.MouseActions.Single(option => option.Key == HistoryMouseAction.ExecuteRecommendedAction);
        viewModel.DoubleClickAction = mouseAction;
        viewModel.MiddleClickAction = mouseAction;

        var saved = config.GetConfig<HistoryShortcutConfig>();
        Assert.AreEqual(HistoryShortcutAction.ExecuteRecommendedAction, saved.Match(new Hotkey(Key.F3)));
        Assert.AreEqual(HistoryMouseAction.ExecuteRecommendedAction, saved.DoubleClickAction);
        Assert.AreEqual(HistoryMouseAction.ExecuteRecommendedAction, saved.MiddleClickAction);
        Assert.AreEqual(HistoryShortcutAction.CopyAndPaste, saved.Match(new Hotkey(Key.Enter)));

        dialog.Setup(service => service.ShowConfirmationAsync(Strings.ResetAllHistoryKeyboardShortcuts,
            Strings.ResetAllHistoryKeyboardShortcutsConfirmMessage)).ReturnsAsync(true);
        await viewModel.ResetHistoryShortcutsCommand.ExecuteAsync(null);
        Assert.AreEqual(Hotkey.Nothing, setting.Hotkey);
        Assert.AreEqual(HistoryMouseAction.ExecuteRecommendedAction, viewModel.DoubleClickAction.Key);
        Assert.AreEqual(HistoryMouseAction.ExecuteRecommendedAction, viewModel.MiddleClickAction.Key);
    }

    [TestMethod]
    public async Task ResetAllKeyboardShortcuts_PreservesIndependentMouseActions()
    {
        CollectionAssert.AreEqual(
            new[] { HistoryMouseAction.Copy, HistoryMouseAction.CopyAndPaste, HistoryMouseAction.ExecuteRecommendedAction },
            viewModel.MouseActions.Select(option => option.Key).ToArray());
        viewModel.DoubleClickAction = viewModel.MouseActions.Single(option => option.Key == HistoryMouseAction.CopyAndPaste);
        viewModel.MiddleClickAction = viewModel.MouseActions.Single(option => option.Key == HistoryMouseAction.Copy);
        var saved = config.GetConfig<HistoryShortcutConfig>();
        Assert.AreEqual(HistoryMouseAction.CopyAndPaste, saved.DoubleClickAction);
        Assert.AreEqual(HistoryMouseAction.Copy, saved.MiddleClickAction);

        var setting = viewModel.ShortcutSettings.Single(row => row.Action == HistoryShortcutAction.Copy);
        viewModel.BeginEditShortcut(setting);
        viewModel.EditingShortcut = Hotkey.Nothing;
        viewModel.SaveShortcutCommand.Execute(null);
        Assert.AreEqual(Hotkey.Nothing, setting.Hotkey);

        dialog.Setup(service => service.ShowConfirmationAsync(Strings.ResetAllHistoryKeyboardShortcuts,
            Strings.ResetAllHistoryKeyboardShortcutsConfirmMessage)).ReturnsAsync(true);
        await viewModel.ResetHistoryShortcutsCommand.ExecuteAsync(null);
        Assert.AreEqual(new Hotkey(Key.Alt, Key.Enter), setting.Hotkey);
        Assert.AreEqual(HistoryMouseAction.CopyAndPaste, viewModel.DoubleClickAction.Key);
        Assert.AreEqual(HistoryMouseAction.Copy, viewModel.MiddleClickAction.Key);
        Assert.IsEmpty(config.GetConfig<HistoryShortcutConfig>().Shortcuts);
        Assert.IsTrue(viewModel.ShortcutSettings.All(row => !row.IsModified));
    }

    [TestMethod]
    public async Task ResetAllKeyboardShortcuts_WaitsForConfirmationAndPreservesSettingsOnCancel()
    {
        config.SetConfig(new HistoryShortcutConfig
        {
            Shortcuts = { [HistoryShortcutAction.Search] = new Hotkey(Key.F2) },
            DoubleClickAction = HistoryMouseAction.ExecuteRecommendedAction
        });
        var before = config.GetConfig<HistoryShortcutConfig>();
        var confirmation = new TaskCompletionSource<bool>();
        dialog.Setup(service => service.ShowConfirmationAsync(Strings.ResetAllHistoryKeyboardShortcuts,
            Strings.ResetAllHistoryKeyboardShortcutsConfirmMessage)).Returns(confirmation.Task);

        var reset = viewModel.ResetHistoryShortcutsCommand.ExecuteAsync(null);
        Assert.IsFalse(reset.IsCompleted);
        Assert.AreEqual(before, config.GetConfig<HistoryShortcutConfig>());

        confirmation.SetResult(false);
        await reset;
        Assert.AreEqual(before, config.GetConfig<HistoryShortcutConfig>());
        Assert.AreEqual(new Hotkey(Key.F2),
            viewModel.ShortcutSettings.Single(row => row.Action == HistoryShortcutAction.Search).Hotkey);
        dialog.Verify(service => service.ShowConfirmationAsync(Strings.ResetAllHistoryKeyboardShortcuts,
            Strings.ResetAllHistoryKeyboardShortcutsConfirmMessage), Times.Once);
    }

    [TestMethod]
    public async Task ResetShortcut_RejectsConflictAndSucceedsAfterOtherBindingChanges()
    {
        var search = viewModel.ShortcutSettings.Single(row => row.Action == HistoryShortcutAction.Search);
        var copy = viewModel.ShortcutSettings.Single(row => row.Action == HistoryShortcutAction.Copy);
        config.SetConfig(new HistoryShortcutConfig
        {
            Shortcuts = new()
            {
                [search.Action] = new Hotkey(Key.F2),
                [copy.Action] = new Hotkey(Key.Ctrl, Key.F)
            }
        });
        var before = config.GetConfig<HistoryShortcutConfig>();
        Assert.IsTrue(search.IsModified);

        await viewModel.ResetShortcutCommand.ExecuteAsync(search);

        Assert.AreEqual(before, config.GetConfig<HistoryShortcutConfig>());
        Assert.AreEqual(new Hotkey(Key.F2), search.Hotkey);
        Assert.IsTrue(search.IsModified);
        dialog.Verify(service => service.ShowMessageAsync(Strings.ResetToDefault,
            string.Format(Strings.HistoryShortcutResetConflict, "Ctrl+F", copy.Name)), Times.Once);

        viewModel.BeginEditShortcut(copy);
        viewModel.EditingShortcut = new Hotkey(Key.F4);
        viewModel.SaveShortcutCommand.Execute(null);
        await viewModel.ResetShortcutCommand.ExecuteAsync(search);

        Assert.AreEqual(new Hotkey(Key.Ctrl, Key.F), search.Hotkey);
        Assert.IsFalse(search.IsModified);
        Assert.AreEqual(new Hotkey(Key.F4), copy.Hotkey);
        Assert.IsTrue(copy.IsModified);
        Assert.IsFalse(config.GetConfig<HistoryShortcutConfig>().Shortcuts.ContainsKey(search.Action));
    }

    [TestMethod]
    public async Task ResetShortcut_UnboundDefaultClearsOnlyThatAction()
    {
        var setting = viewModel.ShortcutSettings.Single(row => row.Action == HistoryShortcutAction.ExecuteRecommendedAction);
        config.SetConfig(new HistoryShortcutConfig
        {
            Shortcuts = new()
            {
                [setting.Action] = new Hotkey(Key.F3),
                [HistoryShortcutAction.Copy] = Hotkey.Nothing
            },
            DoubleClickAction = HistoryMouseAction.ExecuteRecommendedAction
        });
        Assert.IsTrue(setting.IsModified);

        await viewModel.ResetShortcutCommand.ExecuteAsync(setting);

        Assert.AreEqual(Hotkey.Nothing, setting.Hotkey);
        Assert.IsFalse(setting.IsModified);
        Assert.AreEqual(Hotkey.Nothing, config.GetConfig<HistoryShortcutConfig>().GetShortcut(HistoryShortcutAction.Copy));
        Assert.AreEqual(HistoryMouseAction.ExecuteRecommendedAction, viewModel.DoubleClickAction.Key);
        dialog.VerifyNoOtherCalls();
    }
}
