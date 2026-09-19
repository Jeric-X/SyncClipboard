using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Desktop;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using SyncClipboard.Shared.Profiles;
using System.Reflection;
using System.Text;

namespace SyncClipboard.Test.Desktop;

[TestClass]
public class LinuxClipboardTextTests
{
    private readonly string _configDirectory = Path.Combine(Path.GetTempPath(), $"SyncClipboardTextTests-{Guid.NewGuid():N}");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_configDirectory))
            Directory.Delete(_configDirectory, true);
    }

    [TestMethod]
    [DataRow("从历史记录复制的文本\nsecond line")]
    [DataRow("")]
    public async Task UniversalTextWrittenByApplicationCreatesTextProfile(string text)
    {
        var item = new DataTransferItem();
        item.SetText(text);
        var reader = new Mock<IClipboardReader>();
        reader.Setup(r => r.GetTextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => item.TryGetRaw(DataFormat.Text) as string);
        reader.Setup(r => r.GetDataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string format, CancellationToken _) =>
                item.TryGetRaw(DataFormat.CreateBytesPlatformFormat(format))
                ?? item.TryGetRaw(DataFormat.CreateStringPlatformFormat(format)));

        var profile = await ReadLinuxTextProfile(reader.Object, [DataFormat.Text.Identifier, "TIMESTAMP"]);

        Assert.IsInstanceOfType<TextProfile>(profile);
        Assert.AreEqual(text, profile.DisplayText);
        reader.Verify(r => r.GetDataAsync(DataFormat.Text.Identifier, It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow("UTF8_STRING")]
    [DataRow("text/plain;charset=utf-8")]
    [DataRow("TEXT")]
    public async Task NativeTextStillCreatesTextProfileWhenUniversalTextIsUnavailable(string format)
    {
        const string text = "其他应用复制的文本";
        var reader = new Mock<IClipboardReader>();
        reader.Setup(r => r.GetDataAsync(format, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(text));

        var profile = await ReadLinuxTextProfile(reader.Object, [DataFormat.Text.Identifier, format]);

        Assert.IsInstanceOfType<TextProfile>(profile);
        Assert.AreEqual(text, profile.DisplayText);
    }

    private async Task<Profile> ReadLinuxTextProfile(IClipboardReader reader, string[] formats)
    {
        Directory.CreateDirectory(_configDirectory);
        var config = (ConfigManager)Activator.CreateInstance(typeof(ConfigManager),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [Path.Combine(_configDirectory, "config.json"), new SyncClipboardConfigUpgrader()], null)!;
        var clipboard = new MultiSourceClipboardReader([reader], config);
        using var services = new ServiceCollection()
            .AddSingleton(Mock.Of<ILogger>())
            .AddSingleton(clipboard)
            .BuildServiceProvider();
        var factoryType = typeof(AppServices).Assembly.GetType("SyncClipboard.Desktop.ClipboardAva.ClipboardFactory", true)!;
        var factory = (ClipboardFactoryBase)Activator.CreateInstance(factoryType, services)!;

        // Exercise Linux parsing on every test host without accessing its native clipboard.
        var read = factoryType.GetMethod("HandleLinuxClipboard", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var meta = await (Task<ClipboardMetaInfomation>)read.Invoke(factory,
            [formats, true, null, CancellationToken.None])!;
        return await factory.CreateProfileFromMeta(meta, CancellationToken.None);
    }
}
