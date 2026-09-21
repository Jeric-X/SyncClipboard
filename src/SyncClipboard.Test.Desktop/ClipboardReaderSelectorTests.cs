using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using System.Reflection;

namespace SyncClipboard.Test.Desktop;

[TestClass]
public class ClipboardReaderSelectorTests
{
    private static readonly string[] SourceNames = ["Avalonia", "xclip", "wl-clipboard"];
    private ConfigurationTestServices _migrationServices = null!;
    private DirectoryInfo _directory = null!;
    private ConfigManager _config = null!;

    [TestInitialize]
    public void Initialize()
    {
        _migrationServices = new ConfigurationTestServices();
        _directory = Directory.CreateTempSubdirectory("SyncClipboard-reader-");
        _config = (ConfigManager)Activator.CreateInstance(typeof(ConfigManager),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [Path.Combine(_directory.FullName, "config.json"), _migrationServices.Upgrader], null)!;
    }

    [TestCleanup]
    public void Cleanup()
    {
        _migrationServices.Dispose();
        _directory.Delete(true);
    }

    [TestMethod]
    [DataRow(ClipboardReadMethod.Avalonia, "Avalonia")]
    [DataRow(ClipboardReadMethod.XClip, "xclip")]
    [DataRow(ClipboardReadMethod.WlClipboard, "wl-clipboard")]
    public async Task OnlySelectedReaderIsUsedForEveryFormat(ClipboardReadMethod method, string name)
    {
        _config.SetConfig(new ClipboardFactoryConfig { ReadMethod = method });
        var sources = CreateSources();
        var selected = sources.Single(source => source.Object.SourceName == name);
        selected.Setup(source => source.GetFormatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(["text/plain"]);
        selected.Setup(source => source.GetTextAsync(It.IsAny<CancellationToken>())).ReturnsAsync("text");
        selected.Setup(source => source.GetDataAsync("image/png", It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 1 });
        var reader = new ClipboardReaderSelector(sources.Select(source => source.Object), _config, isLinux: true);
        foreach (var source in sources) source.Invocations.Clear();

        Assert.AreEqual("text/plain", (await reader.GetFormatsAsync(CancellationToken.None))!.Single());
        Assert.AreEqual("text", await reader.GetTextAsync(CancellationToken.None));
        Assert.IsInstanceOfType<byte[]>(await reader.GetDataAsync("image/png", CancellationToken.None));
        await reader.GetBitmapAsync(CancellationToken.None);
        await reader.GetFilesAsync(CancellationToken.None);
        Assert.HasCount(5, selected.Invocations);
        foreach (var source in sources.Where(source => source != selected)) source.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task NullAndErrorsDoNotFallBackToAnotherReader()
    {
        var sources = CreateSources();
        sources[1].Setup(source => source.GetTextAsync(It.IsAny<CancellationToken>())).ReturnsAsync("fallback");
        var reader = new ClipboardReaderSelector(sources.Select(source => source.Object), _config, isLinux: true);
        foreach (var source in sources) source.Invocations.Clear();

        Assert.IsNull(await reader.GetTextAsync(CancellationToken.None));
        sources[0].Setup(source => source.GetTextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Cannot access clipboard"));
        await Assert.ThrowsAsync<IOException>(() => reader.GetTextAsync(CancellationToken.None));
        sources[1].VerifyNoOtherCalls();
        sources[2].VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task SelectionChangesTakeEffectAndMissingReaderDoesNotFallBack()
    {
        var source = CreateSources()[0];
        source.Setup(reader => reader.GetTextAsync(It.IsAny<CancellationToken>())).ReturnsAsync("text");
        var reader = new ClipboardReaderSelector([source.Object], _config, isLinux: true);
        Assert.AreEqual("text", await reader.GetTextAsync(CancellationToken.None));
        _config.SetConfig(new ClipboardFactoryConfig { ReadMethod = ClipboardReadMethod.WlClipboard });
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetTextAsync(CancellationToken.None));
    }

    [TestMethod]
    public void NonLinuxPlatformsKeepUsingAvalonia()
    {
        _config.SetConfig(new ClipboardFactoryConfig { ReadMethod = ClipboardReadMethod.WlClipboard });
        var reader = new ClipboardReaderSelector([CreateSources()[0].Object], _config, isLinux: false);
        Assert.AreEqual("Avalonia", reader.SourceName);
    }

    private static Mock<IClipboardReader>[] CreateSources() =>
        SourceNames.Select(name =>
        {
            var reader = new Mock<IClipboardReader>();
            reader.SetupGet(source => source.SourceName).Returns(name);
            return reader;
        }).ToArray();
}
