using Moq;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileActionBuilderTest
{
    private readonly ProfileActionBuilder _builder = new(null!, new TestProfileEnv());
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PlainTextContentActionCopiesText()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var copied = new TaskCompletionSource<ClipboardMetaInfomation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboardSetter = new Mock<IClipboardSetter<TextProfile>>();
        clipboardSetter.Setup(setter => setter.SetLocalClipboard(
                It.IsAny<ClipboardMetaInfomation>(), It.IsAny<CancellationToken>()))
            .Callback<ClipboardMetaInfomation, CancellationToken>((metadata, _) => copied.TrySetResult(metadata))
            .Returns(Task.CompletedTask);
        var services = new Mock<IServiceProvider>();
        services.Setup(provider => provider.GetService(typeof(IClipboardSetter<TextProfile>)))
            .Returns(clipboardSetter.Object);
        var dispatcher = new Mock<IThreadDispatcher>();
        dispatcher.Setup(instance => instance.RunOnMainThreadAsync(It.IsAny<Func<Task>>()))
            .Returns<Func<Task>>(callback => callback());
        var profileEnv = new TestProfileEnv();
        var setter = new LocalClipboardSetter(services.Object, dispatcher.Object, profileEnv);
        var builder = new ProfileActionBuilder(setter, profileEnv);
        var action = await builder.GetPrimaryAction(new TextProfile("plain text"), token);

        Assert.AreEqual(Strings.Copy, action?.Text);
        Assert.IsNotNull(action?.Action);

        action.Action();

        var metadata = await copied.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.AreEqual("plain text", metadata.Text);
        clipboardSetter.Verify(instance => instance.SetLocalClipboard(
            It.IsAny<ClipboardMetaInfomation>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UrlContentActionOpensUrl()
    {
        var action = await _builder.GetPrimaryAction(new TextProfile("https://example.com/path"), CancellationToken.None);

        Assert.AreEqual(Strings.OpenInBrowser, action?.Text);
    }

    [TestMethod]
    public async Task FileContentActionOpensFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "notification-test.txt");
        var action = await _builder.GetPrimaryAction(new FileProfile(path), CancellationToken.None);

        Assert.AreEqual(Strings.Open, action?.Text);
    }

    [TestMethod]
    public async Task ImageContentActionOpensImage()
    {
        var path = Path.Combine(Path.GetTempPath(), "recommended-action-test.png");
        var action = await _builder.GetPrimaryAction(new ImageProfile(path), CancellationToken.None);

        Assert.AreEqual(Strings.Open, action?.Text);
    }

    [TestMethod]
    public async Task FolderContentActionOpensContainingFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "notification-test-folder");
        var action = await _builder.GetPrimaryAction(new GroupProfile([path]), CancellationToken.None);

        Assert.AreEqual(Strings.OpenFolder, action?.Text);
    }

    [TestMethod]
    public async Task LongTextActionsUseLocalizedText()
    {
        var profile = new TextProfile(new string(' ', 10240) + "https://example.com/path");

        var actions = await _builder.Build(profile, CancellationToken.None);
        var primaryAction = await _builder.GetPrimaryAction(profile, CancellationToken.None);

        Assert.IsTrue(actions.Any(action => action.Text == Strings.OpenInBrowser));
        Assert.AreEqual(Strings.OpenInBrowser, primaryAction?.Text);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GroupActionsLocalizeTransferArchiveBeforeUsingPaths(bool primary)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var directory = Path.Combine(Path.GetTempPath(), $"SyncClipboard-Actions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "source.txt");
            await File.WriteAllTextAsync(sourcePath, "content", token);
            var sourceProfile = new GroupProfile([sourcePath]);
            var archivePath = (await sourceProfile.PrepareTransferData(directory, token))?.Path;
            Assert.IsNotNull(archivePath);
            var profile = new GroupProfile([], await sourceProfile.GetHash(token), archivePath, sourceProfile.TransferDataHash);
            Assert.IsEmpty(profile.Files);

            if (primary)
            {
                var action = await _builder.GetPrimaryAction(profile, token);
                Assert.AreEqual(Strings.OpenFolder, action?.Text);
            }
            else
            {
                var actions = await _builder.Build(profile, token);
                Assert.IsTrue(actions.Any(action => action.Text == Strings.OpenFolder));
                Assert.IsTrue(actions.Any(action => action.Text == Strings.Open));
            }

            var extractedPath = profile.Files.Single();
            Assert.AreEqual("source.txt", Path.GetFileName(extractedPath));
            Assert.AreEqual("content", await File.ReadAllTextAsync(extractedPath, token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TestProfileEnv : IProfileEnv
    {
        public string GetPersistentDir() => Path.GetTempPath();

        public string GetHistoryPersistentDir() => Path.GetTempPath();
    }
}
