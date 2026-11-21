using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test.Client.Clipboard;

[TestClass]
public class TextFilterSettingsTests
{
    [TestMethod]
    public void ChangingModeKeepsBothListsAndOnlyConfirmedEditsAreSaved()
    {
        using var services = new ConfigurationTestServices();
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            var config = new ConfigManager(path, services.Upgrader);
            TextFilterConfig Reload() => new ConfigManager(path, services.Upgrader).GetConfig<TextFilterConfig>();
            var original = new TextFilterRule("private", ExcludeFromHistory: true);
            config.SetConfig(new TextFilterConfig { FilterMode = "BlackList", BlackList = [original] });
            var settings = new TextSyncFilterSettingViewModel(config);

            var item = settings.Rules.Single();
            var draft = settings.CreateEditor(item);
            draft.Pattern = "^private$";
            draft.UseRegex = true;
            Assert.AreEqual(original, Reload().BlackList.Single());
            Assert.IsTrue(settings.TrySaveRule(item, draft));
            Assert.AreEqual(new TextFilterRule("^private$", true, true), Reload().BlackList.Single());

            settings.FilterMode = FileSyncFilterSettingViewModel.Modes.Single(mode => mode.Key == "WhiteList");
            Assert.AreEqual("WhiteList", Reload().FilterMode);
            var addition = settings.CreateEditor(null);
            addition.Pattern = "share";
            Assert.IsTrue(settings.TrySaveRule(null, addition));
            Assert.AreEqual(new TextFilterRule("share"), Reload().WhiteList.Single());
            settings.DeleteRule(settings.Rules.Single());
            Assert.IsEmpty(Reload().WhiteList);
            Assert.AreEqual(new TextFilterRule("^private$", true, true), Reload().BlackList.Single());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
