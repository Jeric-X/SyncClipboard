using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop.ClipboardAva;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using System.Text;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[DoNotParallelize]
public class AvaloniaClipboardReaderTests
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    [DataRow("plain text")]
    [DataRow("中文文本\nsecond line")]
    [DataRow("")]
    public async Task UniversalTextCanBeReadByItsListedFormat(string text)
    {
        await WithClipboardAsync(async (clipboard, reader) =>
        {
            await clipboard.SetTextAsync(text);
            var formats = await reader.GetFormatsAsync(CancellationToken.None);
            CollectionAssert.Contains(formats!, DataFormat.Text.Identifier);

            var result = await reader.GetDataAsync(DataFormat.Text.Identifier, CancellationToken.None);

            Assert.IsInstanceOfType<string>(result);
            Assert.AreEqual(text, result);
        });
    }

    [TestMethod]
    [DataRow("TEXT")]
    [DataRow("UTF8_STRING")]
    [DataRow("text/plain;charset=utf-8")]
    public async Task PlatformTextBytesRemainSeparateFromUniversalText(string format)
    {
        await WithClipboardAsync(async (clipboard, reader) =>
        {
            var bytes = Encoding.UTF8.GetBytes("原生格式文本");
            var item = new DataTransferItem();
            item.SetText("universal text");
            item.Set(DataFormat.CreateBytesPlatformFormat(format), bytes);
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);

            var result = await reader.GetDataAsync(format, CancellationToken.None);

            Assert.IsInstanceOfType<byte[]>(result);
            CollectionAssert.AreEqual(bytes, (byte[])result);
        });
    }

    [TestMethod]
    public async Task PlatformStringRemainsReadableWithoutUniversalText()
    {
        await WithClipboardAsync(async (clipboard, reader) =>
        {
            const string html = "<p>text</p>";
            var item = new DataTransferItem();
            item.Set(DataFormat.CreateStringPlatformFormat("text/html"), html);
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);

            Assert.AreEqual(html, await reader.GetDataAsync("text/html", CancellationToken.None));
            Assert.IsNull(await reader.GetDataAsync(DataFormat.Text.Identifier, CancellationToken.None));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PlatformFormatNamedTextUsesItsAdvertisedType(bool useBytes)
    {
        await WithClipboardAsync(async (clipboard, reader) =>
        {
            const string text = "platform text";
            var bytes = Encoding.UTF8.GetBytes(text);
            var item = new DataTransferItem();
            if (useBytes)
                item.Set(DataFormat.CreateBytesPlatformFormat(DataFormat.Text.Identifier), bytes);
            else
                item.Set(DataFormat.CreateStringPlatformFormat(DataFormat.Text.Identifier), text);
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);

            var result = await reader.GetDataAsync(DataFormat.Text.Identifier, CancellationToken.None);

            if (useBytes)
            {
                Assert.IsInstanceOfType<byte[]>(result);
                CollectionAssert.AreEqual(bytes, (byte[])result);
            }
            else
            {
                Assert.AreEqual(text, result);
            }
        });
    }

    [TestMethod]
    public async Task ReadsFirstItemSupportingRequestedFormat()
    {
        await WithClipboardAsync(async (clipboard, reader) =>
        {
            var htmlItem = new DataTransferItem();
            htmlItem.Set(DataFormat.CreateStringPlatformFormat("text/html"), "<p>html</p>");
            var textItem = new DataTransferItem();
            textItem.SetText("first text");
            var anotherTextItem = new DataTransferItem();
            anotherTextItem.SetText("second text");
            var data = new DataTransfer();
            data.Add(htmlItem);
            data.Add(textItem);
            data.Add(anotherTextItem);
            await clipboard.SetDataAsync(data);

            Assert.AreEqual("first text", await reader.GetDataAsync(DataFormat.Text.Identifier, CancellationToken.None));
        });
    }

    [TestMethod]
    [DataRow("public.html", false)]
    [DataRow("public.html", true)]
    [DataRow("text/html", false)]
    [DataRow("text/html", true)]
    public async Task HtmlStringIsReadableFromStringOrUtf8Bytes(string format, bool useBytes)
    {
        await WithClipboardAsync(async (clipboard, reader) =>
        {
            const string html = "<p>中文 HTML 🌍</p>\n";
            var item = new DataTransferItem();
            if (useBytes)
                item.Set(DataFormat.CreateBytesPlatformFormat(format), Encoding.UTF8.GetBytes(html));
            else
                item.Set(DataFormat.CreateStringPlatformFormat(format), html);
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);

            Assert.AreEqual(html, await reader.GetStringAsync(format, CancellationToken.None));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyStringIsPreservedAndMissingFormatReturnsNull(bool useBytes)
    {
        await WithClipboardAsync(async (clipboard, reader) =>
        {
            const string format = "text/html";
            var item = new DataTransferItem();
            if (useBytes)
                item.Set(DataFormat.CreateBytesPlatformFormat(format), []);
            else
                item.Set(DataFormat.CreateStringPlatformFormat(format), string.Empty);
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);

            Assert.AreEqual(string.Empty, await reader.GetStringAsync(format, CancellationToken.None));
            Assert.IsNull(await reader.GetStringAsync("missing-format", CancellationToken.None));
        });
    }

    [TestMethod]
    public async Task ReadingOwnPackageDoesNotDisposeClipboardResources()
    {
        await WithOriginalPackageClipboardAsync(async (clipboard, reader) =>
        {
            var file = new Mock<IStorageItem>();
            var item = new DataTransferItem();
            item.SetText("仍在剪贴板中");
            item.SetFile(file.Object);
            item.Set(DataFormat.CreateBytesPlatformFormat("text/html"), Encoding.UTF8.GetBytes("<p>内容</p>"));
            using var package = new AutoDisposeDataTransfer(new DataTransfer());
            package.Data.Add(item);
            await clipboard.SetDataAsync(package);

            // 模拟 X11 读取自身剪贴板时返回原包，而非独立读取对象。
            Assert.AreSame(package, await clipboard.TryGetDataAsync());
            Assert.Contains(DataFormat.Text.Identifier, (await reader.GetFormatsAsync(CancellationToken.None))!);
            Assert.AreEqual("仍在剪贴板中", await reader.GetTextAsync(CancellationToken.None));
            Assert.AreSame(file.Object, (await reader.GetFilesAsync(CancellationToken.None))![0]);
            Assert.AreEqual("<p>内容</p>", await reader.GetStringAsync("text/html", CancellationToken.None));
            Assert.IsNull(await reader.GetDataAsync("missing-format", CancellationToken.None));
            Assert.IsNull(await reader.GetBitmapAsync(CancellationToken.None));
            var current = await clipboard.TryGetDataAsync();
            Assert.AreEqual("仍在剪贴板中", await current!.TryGetTextAsync());
            file.Verify(value => value.Dispose(), Times.Never);

            package.Dispose();
            file.Verify(value => value.Dispose(), Times.Once);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReadBitmapCanBeDisposedWithoutInvalidatingClipboardBitmap(bool useRawFormat)
    {
        await WithOriginalPackageClipboardAsync(async (clipboard, reader) =>
        {
            using var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96));
            using var package = ClipboardBitmapLifetimeTests.CreatePackage(bitmap);
            await clipboard.SetDataAsync(package);

            var result = useRawFormat
                ? (Bitmap?)await reader.GetDataAsync(DataFormat.Bitmap.Identifier, CancellationToken.None)
                : await reader.GetBitmapAsync(CancellationToken.None);
            Assert.IsNotNull(result);
            Assert.AreNotSame(bitmap, result);
            Assert.AreEqual(bitmap.PixelSize, result.PixelSize);
            result.Dispose();

            Assert.AreEqual(new PixelSize(2, 2), bitmap.PixelSize);
            Assert.AreSame(bitmap, await ((IAsyncDataTransfer)package).TryGetBitmapAsync());
        });
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ReadingOnlyDisposesAnOwnedReadPackage(bool borrowed, bool fails)
    {
        await WithOriginalPackageClipboardAsync(async (clipboard, reader) =>
        {
            var item = new DataTransferItem();
            item.SetText("内容");
            if (fails)
                item.Set(DataFormat.Text, () => throw new IOException("读取失败"));
            var data = new DataTransfer();
            data.Add(item);
            using var ownPackage = new AutoDisposeDataTransfer(data);
            var readPackage = new Mock<IAsyncDataTransfer>();
            readPackage.SetupGet(value => value.Items).Returns(((IAsyncDataTransfer)data).Items);
            readPackage.SetupGet(value => value.Formats).Returns(data.Formats);
            await clipboard.SetDataAsync(borrowed ? ownPackage : readPackage.Object);

            try
            {
                if (fails)
                    await Assert.ThrowsAsync<IOException>(() => reader.GetTextAsync(CancellationToken.None));
                else
                    Assert.AreEqual("内容", await reader.GetTextAsync(CancellationToken.None));
            }
            finally
            {
                // 工厂仅用于模拟读取异常，清理数据包前恢复普通值。
                item.SetText("内容");
            }

            if (borrowed)
                Assert.Contains(DataFormat.Text, ownPackage.Formats);
            else
                readPackage.Verify(value => value.Dispose(), Times.Once);
        });
    }

    [TestMethod]
    public async Task ReadingOwnTimestampDoesNotDisposePackage()
    {
        await WithOriginalPackageClipboardAsync(async (clipboard, reader) =>
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Inconclusive("时间戳读取仅用于 Linux。");
                return;
            }

            var item = new DataTransferItem();
            item.Set(DataFormat.CreateBytesPlatformFormat(Format.TimeStamp), Encoding.UTF8.GetBytes("123\n"));
            using var package = new AutoDisposeDataTransfer(new DataTransfer());
            package.Data.Add(item);
            await clipboard.SetDataAsync(package);

            Assert.AreEqual(123, await reader.GetTimeStamp(CancellationToken.None));
            Assert.AreEqual(123, await reader.GetDataAsync(Format.TimeStamp, CancellationToken.None));
            Assert.HasCount(1, package.Items);
        });
    }

    private static Task WithOriginalPackageClipboardAsync(Func<IClipboard, IClipboardReader, Task> action)
    {
        return WithClipboardAsync(async (_, _) =>
        {
            IAsyncDataTransfer? current = null;
            var clipboard = new Mock<IClipboard>();
            clipboard.Setup(value => value.SetDataAsync(It.IsAny<IAsyncDataTransfer?>()))
                .Callback<IAsyncDataTransfer?>(data => current = data)
                .Returns(Task.CompletedTask);
            clipboard.Setup(value => value.TryGetDataAsync()).Returns(() => Task.FromResult(current));
            await action(clipboard.Object, new AvaloniaClipboardReader(clipboard.Object));
        });
    }

    private static async Task WithClipboardAsync(Func<IClipboard, IClipboardReader, Task> action)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaClipboardReaderTests));
        await session.Dispatch(async () =>
        {
            var window = new TestWindow();
            try
            {
                var clipboard = window.Clipboard!;
                await action(clipboard, new AvaloniaClipboardReader(clipboard));
                await clipboard.ClearAsync();
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private sealed class TestWindow : Window, IMainWindow
    {
        public void NavigateTo(PageDefinition page, NavigationTransitionEffect effect, object? para) =>
            throw new NotSupportedException();
        public void OpenPage(PageDefinition page, object? para = null) => throw new NotSupportedException();
        public void NavigateToLastLevel() => throw new NotSupportedException();
        public void NavigateToNextLevel(PageDefinition page, object? para) => throw new NotSupportedException();
    }
}
