using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop;
using SyncClipboard.Desktop.ClipboardAva;
using SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;
using SyncClipboard.Shared.Profiles;
using System.Reflection;

namespace SyncClipboard.Test.Desktop;

[TestClass]
public class ClipboardWriterSelectorTests
{
    private static readonly string[] SourceNames = ["Avalonia", "wl-clipboard"];
    private ConfigurationTestServices _migrationServices = null!;
    private DirectoryInfo _directory = null!;
    private ConfigManager _config = null!;

    [TestInitialize]
    public void Initialize()
    {
        _migrationServices = new ConfigurationTestServices();
        _directory = Directory.CreateTempSubdirectory("SyncClipboard-writer-");
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
    [DataRow(ClipboardWriteMethod.Avalonia, "Avalonia")]
    [DataRow(ClipboardWriteMethod.WlClipboard, "wl-clipboard")]
    public async Task OnlySelectedBackendReceivesTextPackagesAndCancellation(ClipboardWriteMethod method, string name)
    {
        _config.SetConfig(new ClipboardFactoryConfig { WriteMethod = method });
        var sources = CreateSources();
        var writer = new ClipboardWriterSelector(sources.Select(source => source.Object), _config, true);
        var selected = sources.Single(source => source.Object.SourceName == name);
        using var package = new DataTransfer();
        using var cancellation = new CancellationTokenSource();
        foreach (var source in sources) source.Invocations.Clear();

        await writer.SetTextAsync("text", cancellation.Token);
        await writer.SetDataAsync(package, cancellation.Token);

        selected.Verify(source => source.SetTextAsync("text", cancellation.Token), Times.Once);
        selected.Verify(source => source.SetDataAsync(package, cancellation.Token), Times.Once);
        foreach (var source in sources.Where(source => source != selected)) source.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task BackendChangesTakeEffectIndependentlyOfReadSelection()
    {
        var sources = CreateSources();
        var writer = new ClipboardWriterSelector(sources.Select(source => source.Object), _config, true);
        await writer.SetTextAsync("text", CancellationToken.None);

        _config.SetConfig(new ClipboardFactoryConfig
        {
            ReadMethod = ClipboardReadMethod.XClip,
            WriteMethod = ClipboardWriteMethod.WlClipboard
        });
        await writer.SetTextAsync("text", CancellationToken.None);

        foreach (var source in sources)
            source.Verify(value => value.SetTextAsync("text", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task FailureAndCancellationDoNotFallBack()
    {
        _config.SetConfig(new ClipboardFactoryConfig { WriteMethod = ClipboardWriteMethod.WlClipboard });
        var sources = CreateSources();
        var writer = new ClipboardWriterSelector(sources.Select(source => source.Object), _config, true);
        foreach (var source in sources) source.Invocations.Clear();
        using var package = new DataTransfer();
        sources[1].Setup(source => source.SetDataAsync(package, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Wayland connection failed"));
        await Assert.ThrowsAsync<IOException>(() => writer.SetDataAsync(package, CancellationToken.None));

        sources[1].Setup(source => source.SetDataAsync(package, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => writer.SetDataAsync(package, CancellationToken.None));
        sources[0].VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task MissingSelectedBackendDoesNotFallBack()
    {
        _config.SetConfig(new ClipboardFactoryConfig { WriteMethod = ClipboardWriteMethod.WlClipboard });
        var source = CreateSources()[0];
        var writer = new ClipboardWriterSelector([source.Object], _config, true);
        source.Invocations.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SetTextAsync("text", CancellationToken.None));
        source.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task NonLinuxPlatformsKeepUsingAvalonia()
    {
        _config.SetConfig(new ClipboardFactoryConfig { WriteMethod = ClipboardWriteMethod.WlClipboard });
        var source = CreateSources()[0];
        var writer = new ClipboardWriterSelector([source.Object], _config, false);

        await writer.SetTextAsync("text", CancellationToken.None);

        source.Verify(value => value.SetTextAsync("text", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task StartupKeepsProfileSettersAndResolvesWriterWithoutAffectingDragPackages()
    {
        _config.SetConfig(new ClipboardFactoryConfig { WriteMethod = ClipboardWriteMethod.WlClipboard });
        var services = AppServices.ConfigureServices();
        services.AddSingleton(_config);
        services.AddSingleton(Mock.Of<ILogger>());
        using var provider = services.BuildServiceProvider();

        Assert.IsInstanceOfType<TextClipboardSetter>(provider.GetRequiredService<IClipboardSetter<TextProfile>>());
        Assert.IsInstanceOfType<FileClipboardSetter>(provider.GetRequiredService<IClipboardSetter<FileProfile>>());
        Assert.IsInstanceOfType<ImageClipboardSetter>(provider.GetRequiredService<IClipboardSetter<ImageProfile>>());
        Assert.IsInstanceOfType<FileClipboardSetter>(provider.GetRequiredService<IClipboardSetter<GroupProfile>>());
        var writer = provider.GetRequiredService<ClipboardWriterSelector>();
        Assert.AreEqual(OperatingSystem.IsLinux() ? "wl-clipboard" : "Avalonia", writer.SourceName);
        Assert.HasCount(OperatingSystem.IsLinux() ? 2 : 1, provider.GetServices<IClipboardWriter>().ToArray());
        Assert.AreSame(writer, provider.GetRequiredService<IClipboardWriteCapabilities>());
        Assert.AreEqual(!OperatingSystem.IsLinux(), provider.GetRequiredService<IClipboardWriteCapabilities>().SupportsMultipleFormats);

        using var package = new DataTransfer();
        await provider.GetRequiredService<IClipboardSetter<TextProfile>>()
            .FillPackage(package, new ClipboardMetaInfomation { Text = "drag text" });
        Assert.AreEqual("drag text", package.Items.Single().TryGetRaw(DataFormat.Text));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void EasyCopyImageDescriptionTracksWriterWithoutChangingSwitch(bool isLinux)
    {
        IClipboardWriter[] sources = [new AvaloniaClipboardWriter(), new WlClipboardWriter()];
        var writer = new ClipboardWriterSelector(sources, _config, isLinux);
        using var vm = new CliboardAssistantViewModel(_config, null!, writer);
        var descriptionChanged = false;
        vm.PropertyChanged += (_, args) => descriptionChanged |= args.PropertyName == nameof(vm.EasyCopyImageDescription);
        vm.EasyCopyImageSwitchOn = true;

        // Reading through wl-clipboard must not disable a multi-format writer.
        _config.SetConfig(new ClipboardFactoryConfig { ReadMethod = ClipboardReadMethod.WlClipboard });
        Assert.IsTrue(vm.EasyCopyImageSwitchOn);
        Assert.AreEqual(Core.I18n.Strings.ImageAssistantDescription, vm.EasyCopyImageDescription);

        descriptionChanged = false;
        _config.SetConfig(new ClipboardFactoryConfig { WriteMethod = ClipboardWriteMethod.WlClipboard });
        Assert.AreEqual(!isLinux, writer.SupportsMultipleFormats);
        Assert.IsTrue(descriptionChanged);
        Assert.AreEqual(isLinux
            ? string.Format(Core.I18n.Strings.EasyCopyImageUnsupportedWriter, "wl-clipboard")
            : Core.I18n.Strings.ImageAssistantDescription, vm.EasyCopyImageDescription);
        Assert.IsTrue(vm.EasyCopyImageSwitchOn);
        Assert.IsTrue(_config.GetConfig<ClipboardAssistConfig>().EasyCopyImageSwitchOn);

        vm.EasyCopyImageSwitchOn = false;
        Assert.IsFalse(_config.GetConfig<ClipboardAssistConfig>().EasyCopyImageSwitchOn);
        vm.EasyCopyImageSwitchOn = true;
        Assert.IsTrue(_config.GetConfig<ClipboardAssistConfig>().EasyCopyImageSwitchOn);

        _config.SetConfig(new ClipboardFactoryConfig { WriteMethod = ClipboardWriteMethod.Avalonia });
        Assert.IsTrue(writer.SupportsMultipleFormats);
        Assert.AreEqual(Core.I18n.Strings.ImageAssistantDescription, vm.EasyCopyImageDescription);
        Assert.IsTrue(vm.EasyCopyImageSwitchOn);
    }

    private static Mock<IClipboardWriter>[] CreateSources() =>
        SourceNames.Select(name =>
        {
            var source = new Mock<IClipboardWriter>();
            source.SetupGet(value => value.SourceName).Returns(name);
            return source;
        }).ToArray();
}
