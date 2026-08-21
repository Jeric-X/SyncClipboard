using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.I18n;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileActionBuilderTest
{
    private readonly ProfileActionBuilder _builder = new(null!, new TestProfileEnv());

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

    private sealed class TestProfileEnv : IProfileEnv
    {
        public string GetPersistentDir() => Path.GetTempPath();

        public string GetHistoryPersistentDir() => Path.GetTempPath();
    }
}
