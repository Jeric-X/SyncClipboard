using Avalonia.Input;
using ImageMagick;
using SyncClipboard.Core.Models;
using SyncClipboard.Desktop.ClipboardAva;
using SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;
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
        await writer.SetTextAsync(text, CancellationToken.None);
        CollectionAssert.AreEqual(TextArguments, ReadArguments());
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(text), File.ReadAllBytes(DataPath));
    }

    [TestMethod]
    public async Task FilesUseEscapedAbsoluteUrisWithCrLfSeparators()
    {
        var files = new[] { Path.Combine(_directory.FullName, "中文 空格#%.pdf"), Path.Combine(_directory.FullName, "b.pdf") };
        var writer = CreateWriter();
        var uris = string.Join("\n", files.Select(file => new Uri(file).AbsoluteUri));
        using var package = CreatePackage("text/uri-list", Encoding.UTF8.GetBytes(uris));
        await writer.SetDataAsync(package, CancellationToken.None);
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task ImagePackagePrefersPngOverFileAndTextRepresentations(bool sameItem)
    {
        using var original = new MagickImage(MagickColors.Red, 2, 3);
        using var package = new DataTransfer();
        var other = new DataTransferItem();
        other.SetText("image");
        other.Set(DataFormat.CreateBytesPlatformFormat("text/uri-list"), Encoding.UTF8.GetBytes("file:///tmp/image.png"));
        package.Add(other);
        var image = sameItem ? other : new DataTransferItem();
        image.Set(DataFormat.CreateBytesPlatformFormat("image/png"), original.ToByteArray(MagickFormat.Png));
        if (!sameItem) package.Add(image);

        await CreateWriter().SetDataAsync(package, CancellationToken.None);
        CollectionAssert.AreEqual(ImageArguments, ReadArguments());
        using var actual = new MagickImage(File.ReadAllBytes(DataPath));
        Assert.AreEqual(MagickFormat.Png, actual.Format);
        Assert.AreEqual(2u, actual.Width);
        Assert.AreEqual(3u, actual.Height);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FileListTakesPriorityOverEarlierText(bool sameItem)
    {
        using var package = new DataTransfer();
        var text = new DataTransferItem();
        text.SetText("file name");
        package.Add(text);
        var files = sameItem ? text : new DataTransferItem();
        files.Set(DataFormat.CreateBytesPlatformFormat("text/uri-list"), Encoding.UTF8.GetBytes("file:///tmp/a.pdf"));
        if (!sameItem) package.Add(files);

        await CreateWriter().SetDataAsync(package, CancellationToken.None);

        CollectionAssert.AreEqual(FileArguments, ReadArguments());
        Assert.AreEqual("file:///tmp/a.pdf\r\n", File.ReadAllText(DataPath));
    }

    [TestMethod]
    public async Task UnsupportedFormatDoesNotPreventWritingText()
    {
        using var package = CreatePackage("text/html", Encoding.UTF8.GetBytes("<b>hello</b>"));
        var text = new DataTransferItem();
        text.SetText("hello");
        package.Add(text);

        await CreateWriter().SetDataAsync(package, CancellationToken.None);

        CollectionAssert.AreEqual(TextArguments, ReadArguments());
        Assert.AreEqual("hello", File.ReadAllText(DataPath));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("中文\ntext")]
    public async Task TextSetterPackageIsWrittenWithoutTimestamp(string text)
    {
        using var package = new DataTransfer();
        await new TextClipboardSetter().FillPackage(package, new ClipboardMetaInfomation { Text = text });
        var timestamp = new DataTransferItem();
        timestamp.Set(DataFormat.CreateBytesPlatformFormat("TIMESTAMP"), Encoding.UTF8.GetBytes("123"));
        package.Add(timestamp);

        await CreateWriter().SetDataAsync(package, CancellationToken.None);

        CollectionAssert.AreEqual(TextArguments, ReadArguments());
        Assert.AreEqual(text, File.ReadAllText(DataPath));
    }

    [TestMethod]
    [DataRow("TIMESTAMP")]
    [DataRow("text/html")]
    [DataRow("image/jpeg")]
    public async Task UnsupportedPackageDoesNotStartCommand(string format)
    {
        using var package = CreatePackage(format, Encoding.UTF8.GetBytes("123"));
        var writer = CreateWriter();
        await Assert.ThrowsAsync<NotSupportedException>(() => writer.SetDataAsync(package, CancellationToken.None));
        Assert.IsFalse(File.Exists(DataPath));
    }

    [TestMethod]
    public async Task CancelledPackageDoesNotStartCommand()
    {
        using var package = CreatePackage("image/png", [1, 2]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var writer = CreateWriter();
        await Assert.ThrowsAsync<OperationCanceledException>(() => writer.SetDataAsync(package, cancellation.Token));
        Assert.IsFalse(File.Exists(DataPath));
    }

    [TestMethod]
    public async Task FailedCommandReportsExitCodeAndStderr()
    {
        var writer = CreateWriter("echo 'Wayland connection failed' >&2\nexit 7", captureInput: false);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.SetTextAsync(new string('x', 1024 * 1024), CancellationToken.None));
        Assert.Contains("7", error.Message);
        Assert.Contains("Wayland connection failed", error.Message);
    }

    [TestMethod]
    public async Task SuccessDoesNotWaitForBackgroundClipboardOwnerToCloseStderr()
    {
        var writer = CreateWriter("sleep 30 >&2 &\necho $! > \"$dir/child-pid\"\nexit 0");
        await writer.SetTextAsync("test", TestContext.CancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual("test", File.ReadAllText(DataPath));
    }

    [TestMethod]
    public async Task CancellationStopsACommandThatNeverReadsStdin()
    {
        var writer = CreateWriter("echo $$ > \"$dir/child-pid\"\nexec sleep 30", captureInput: false);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            writer.SetTextAsync(new string('x', 1024 * 1024), cancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(3), TestContext.CancellationTokenSource.Token));
    }

    private static DataTransfer CreatePackage(string format, byte[] data)
    {
        var package = new DataTransfer();
        var item = new DataTransferItem();
        item.Set(DataFormat.CreateBytesPlatformFormat(format), data);
        package.Add(item);
        return package;
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
