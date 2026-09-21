using ImageMagick;
using SyncClipboard.Desktop.ClipboardAva;
using System.Diagnostics;
using System.Text;

namespace SyncClipboard.Test.Desktop;

[TestClass]
public class WlClipboardWriterTests
{
    private static readonly string[] TextArguments = ["--type", "text/plain;charset=utf-8"];
    private static readonly string[] FileArguments = ["--type", "text/uri-list"];
    private static readonly string[] ImageArguments = ["--type", "image/png"];
    private DirectoryInfo _directory = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Tests use a Unix executable to simulate wl-copy.");
        _directory = Directory.CreateTempSubdirectory("SyncClipboard-wl-copy-");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_directory is null) return;
        var childPid = Path.Combine(_directory.FullName, "child-pid");
        if (File.Exists(childPid))
        {
            try
            {
                using var child = Process.GetProcessById(int.Parse(File.ReadAllText(childPid)));
                child.Kill();
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        _directory.Delete(true);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("中文\nsecond line\r\n")]
    [DataRow("--clear $(touch unexpected) 'quoted' \"double quoted\"")]
    public async Task TextUsesUtf8StdinWithoutShellExpansionOrNewlineChanges(string text)
    {
        var writer = CreateWriter();
        await writer.WriteTextAsync(text, CancellationToken.None);
        CollectionAssert.AreEqual(TextArguments, ReadArguments());
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(text), File.ReadAllBytes(DataPath));
    }

    [TestMethod]
    public async Task FilesUseEscapedAbsoluteUrisWithCrLfSeparators()
    {
        var files = new[] { Path.Combine(_directory.FullName, "中文 空格#%.pdf"), Path.Combine(_directory.FullName, "b.pdf") };
        var writer = CreateWriter();
        await writer.WriteFilesAsync(files, CancellationToken.None);
        CollectionAssert.AreEqual(FileArguments, ReadArguments());
        var text = File.ReadAllText(DataPath);
        var lines = text.Split("\r\n");
        Assert.AreEqual(3, lines.Length);
        Assert.AreEqual("", lines[2]);
        Assert.Contains("%20", lines[0]);
        Assert.Contains("%23", lines[0]);
        Assert.Contains("%25", lines[0]);
        Assert.AreEqual(files[0], new Uri(lines[0]).LocalPath);
        Assert.AreEqual(files[1], new Uri(lines[1]).LocalPath);
    }

    [TestMethod]
    public async Task ImageIsConvertedToPng()
    {
        var path = Path.Combine(_directory.FullName, "image.bmp");
        using (var original = new MagickImage(MagickColors.Red, 2, 3))
            original.Write(path);
        await CreateWriter().WriteImageAsync(path, CancellationToken.None);
        CollectionAssert.AreEqual(ImageArguments, ReadArguments());
        using var actual = new MagickImage(File.ReadAllBytes(DataPath));
        Assert.AreEqual(MagickFormat.Png, actual.Format);
        Assert.AreEqual(2u, actual.Width);
        Assert.AreEqual(3u, actual.Height);
    }

    [TestMethod]
    public async Task FailedCommandReportsExitCodeAndStderr()
    {
        var writer = CreateWriter("echo 'Wayland connection failed' >&2\nexit 7", captureInput: false);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.WriteTextAsync(new string('x', 1024 * 1024), CancellationToken.None));
        Assert.Contains("7", error.Message);
        Assert.Contains("Wayland connection failed", error.Message);
    }

    [TestMethod]
    public async Task SuccessDoesNotWaitForBackgroundClipboardOwnerToCloseStderr()
    {
        var writer = CreateWriter("sleep 30 >&2 &\necho $! > \"$dir/child-pid\"\nexit 0");
        await writer.WriteTextAsync("test", TestContext.CancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual("test", File.ReadAllText(DataPath));
    }

    [TestMethod]
    public async Task CancellationStopsACommandThatNeverReadsStdin()
    {
        var writer = CreateWriter("echo $$ > \"$dir/child-pid\"\nexec sleep 30", captureInput: false);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            writer.WriteTextAsync(new string('x', 1024 * 1024), cancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(3), TestContext.CancellationTokenSource.Token));
    }

    private string DataPath => Path.Combine(_directory.FullName, "data");
    private string[] ReadArguments() => File.ReadAllLines(Path.Combine(_directory.FullName, "args"));

    private WlClipboardWriter CreateWriter(string ending = "exit 0", bool captureInput = true)
    {
        var path = Path.Combine(_directory.FullName, "wl-copy");
        var script = "#!/bin/sh\ndir=${0%/*}\nprintf '%s\\n' \"$@\" > \"$dir/args\"\n";
        if (captureInput) script += "cat > \"$dir/data\"\n";
        File.WriteAllText(path, script + ending + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new WlClipboardWriter(path);
    }
}
