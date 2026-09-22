using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
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
        viewModel = new HistorySettingViewModel(config, null!, Mock.Of<IMainWindowDialog>(), factory);
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
    public void RecommendedAction_CanBeAssignedToKeyboardAndBothMouseGestures()
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

        viewModel.ResetHistoryShortcutsCommand.Execute(null);
        Assert.AreEqual(Hotkey.Nothing, setting.Hotkey);
        Assert.AreEqual(HistoryMouseAction.Copy, viewModel.DoubleClickAction.Key);
        Assert.AreEqual(HistoryMouseAction.CopyAndPaste, viewModel.MiddleClickAction.Key);
    }

    [TestMethod]
    public void MouseActions_SaveIndependentlyAndResetWithKeyboardBindings()
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

        viewModel.ResetHistoryShortcutsCommand.Execute(null);
        Assert.AreEqual(new Hotkey(Key.Alt, Key.Enter), setting.Hotkey);
        Assert.AreEqual(HistoryMouseAction.Copy, viewModel.DoubleClickAction.Key);
        Assert.AreEqual(HistoryMouseAction.CopyAndPaste, viewModel.MiddleClickAction.Key);
        Assert.AreEqual(new HistoryShortcutConfig(), config.GetConfig<HistoryShortcutConfig>());
    }
}
