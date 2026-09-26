using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Desktop.ClipboardAva;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using System.Reflection;

namespace SyncClipboard.Test.Desktop;

[TestClass]
public class ClipboardListenerTests
{
    [TestMethod]
    public void RepeatedContentWithNewFingerprintIsReadOnlyOnce()
    {
        VerifyReadCount(useFingerprint: true, expectedReads: 2);
    }

    [TestMethod]
    public void UnavailableFingerprintContinuesReadingClipboard()
    {
        VerifyReadCount(useFingerprint: false, expectedReads: 5);
    }

    private static void VerifyReadCount(bool useFingerprint, int expectedReads)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("This regression exercises macOS content equality without a timestamp.");
            return;
        }

        using var migration = new ConfigurationTestServices();
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-listener-");
        try
        {
            var config = (ConfigManager)Activator.CreateInstance(typeof(ConfigManager),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                [Path.Combine(directory.FullName, "config.json"), migration.Upgrader], null)!;
            var reader = new Mock<IClipboardReader>();
            reader.SetupGet(value => value.SourceName).Returns("Avalonia");
            reader.Setup(value => value.GetFormatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Format.MacText]);
            reader.Setup(value => value.GetTextAsync(It.IsAny<CancellationToken>())).ReturnsAsync("same content");
            var logger = new Mock<ILogger>();
            logger.Setup(value => value.WriteAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            using var services = new ServiceCollection()
                .AddSingleton(logger.Object)
                .AddSingleton(new ClipboardReaderSelector([reader.Object], config))
                .BuildServiceProvider();
            var factory = new ClipboardFactory(services);
            var fingerprint = new Mock<IClipboardFingerprintProvider>();
            int? currentFingerprint = useFingerprint ? 1 : null;
            fingerprint.Setup(value => value.GetClipboardFingerprint(It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(currentFingerprint));
            using var listener = new ClipboardListener(factory, fingerprint.Object, logger.Object);

            // 模拟读取均同步完成，确保本次触发结束后才开始下一次。
            listener.TriggerClipboardChangedEvent();
            currentFingerprint = useFingerprint ? 2 : null;
            for (int i = 0; i < 4; i++)
                listener.TriggerClipboardChangedEvent();

            reader.Verify(value => value.GetFormatsAsync(It.IsAny<CancellationToken>()), Times.Exactly(expectedReads));
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
