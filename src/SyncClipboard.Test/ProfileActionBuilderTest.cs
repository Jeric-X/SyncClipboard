using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.I18n;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileActionBuilderTest
{
    private readonly ProfileActionBuilder _builder = new(null!, new TestProfileEnv());
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PlainTextHasNoContentAction()
    {
        var action = await _builder.GetPrimaryAction(new TextProfile("plain text"), CancellationToken.None);

        Assert.IsNull(action);
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
            var extractedPath = Path.Combine(archivePath[..^4], "source.txt");
            Assert.IsFalse(File.Exists(extractedPath));

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

            CollectionAssert.AreEqual(new[] { extractedPath }, profile.Files);
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
