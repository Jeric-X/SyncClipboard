using Microsoft.Extensions.DependencyInjection;
using Moq;
using NativeNotification.Interface;
using SharpHook.Data;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test;

[TestClass]
public class LinuxInputBackendTests
{
    [TestMethod]
    [DataRow(LinuxMode.AutoXRecord)]
    [DataRow(LinuxMode.AutoLowLevel)]
    [DataRow(LinuxMode.XRecord)]
    public void Selection_PersistsAcrossRestart_AndPreservesOtherSettings(LinuxMode mode)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "config.json");
            using var services = new ServiceCollection()
                .AddSingleton(Mock.Of<ILogger>())
                .BuildServiceProvider();
            var staticConfig = new StaticConfig(Mock.Of<INotificationManager>());
            var config = new ConfigManager(path, new SyncClipboardConfigUpgrader());
            config.SetConfig(new ProgramConfig { LogRemainDays = 17 });
            var viewModel = new SystemSettingViewModel(config, staticConfig, services);
            Assert.AreEqual(LinuxMode.AutoXRecord, viewModel.LinuxInputMode.Key);

            viewModel.LinuxInputMode = SystemSettingViewModel.LinuxInputModes.Single(x => x.Key == mode);
            // A subsequent change must not overwrite the selected backend.
            viewModel.HideWindowOnStartUp = true;

            var reloaded = new ConfigManager(path, new SyncClipboardConfigUpgrader());
            var reopened = new SystemSettingViewModel(reloaded, staticConfig, services);
            Assert.AreEqual(mode, reopened.LinuxInputMode.Key);
            Assert.AreEqual(mode, reloaded.GetConfig<ProgramConfig>().LinuxInputMode);
            Assert.AreEqual(17u, reopened.LogRemainDays);
            Assert.IsTrue(reopened.HideWindowOnStartUp);

            // External config changes must also update the displayed selection.
            reloaded.SetConfig(reloaded.GetConfig<ProgramConfig>() with { LinuxInputMode = LinuxMode.AutoLowLevel });
            Assert.AreEqual(LinuxMode.AutoLowLevel, reopened.LinuxInputMode.Key);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
