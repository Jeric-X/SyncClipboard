using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
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

    private static async Task WithClipboardAsync(Func<IClipboard, IClipboardReader, Task> action)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaClipboardReaderTests));
        await session.Dispatch(async () =>
        {
            var window = new TestWindow();
            try
            {
                var clipboard = window.Clipboard!;
                await action(clipboard, new AvaloniaClipboardReader(window));
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
