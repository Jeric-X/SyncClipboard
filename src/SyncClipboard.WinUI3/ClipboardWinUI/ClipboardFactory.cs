using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal partial class ClipboardFactory : ClipboardFactoryBase
{
    protected override ILogger Logger { get; set; }
    protected override IServiceProvider ServiceProvider { get; set; }
    protected override IWebDav WebDav { get; set; }

    private const string LOG_TAG = nameof(ClipboardFactory);

    private delegate Task FormatHandler(DataPackageView ClipboardData, ClipboardMetaInfomation meta, CancellationToken ctk);
    private static List<KeyValuePair<string, FormatHandler>> FormatHandlerlist => new Dictionary<string, FormatHandler>
    {
        [StandardDataFormats.Text] = HanleText,
        ["DeviceIndependentBitmap"] = HanleDib,
        [StandardDataFormats.Bitmap] = HanleBitmap,
        [StandardDataFormats.Html] = HanleHtml,
        [StandardDataFormats.StorageItems] = HanleFiles,
        ["Preferred DropEffect"] = HanleDropEffect,
        ["Object Descriptor"] = HanleObjectDescriptor,
    }.ToList();

    private static async Task HanleBitmap(DataPackageView ClipboardData, ClipboardMetaInfomation meta, CancellationToken ctk)
    {
        if (meta.Image is not null) return;
        var bitmapStrem = await ClipboardData.GetBitmapAsync();
        using var randomStream = await bitmapStrem.OpenReadAsync();
        using MagickImage image = new(randomStream.AsStream());
        meta.Image = WinBitmap.FromImage(image.ToBitmap());
    }

    private static async Task HanleDib(DataPackageView ClipboardData, ClipboardMetaInfomation meta, CancellationToken ctk)
    {
        var res = await ClipboardData.GetDataAsync("DeviceIndependentBitmap");
        using var stream = res.As<IRandomAccessStream>().AsStream();
        using MagickImage image = new(stream);
        meta.Image = WinBitmap.FromImage(image.ToBitmap());
    }

    private static async Task HanleDropEffect(DataPackageView ClipboardData, ClipboardMetaInfomation meta, CancellationToken ctk)
    {
        var res = await ClipboardData.GetDataAsync("Preferred DropEffect");
        using IRandomAccessStream randomAccessStream = res.As<IRandomAccessStream>();
        meta.Effects = (DragDropEffects?)randomAccessStream.AsStreamForRead().ReadByte();
    }

    private async static Task HanleObjectDescriptor(DataPackageView ClipboardData, ClipboardMetaInfomation meta, CancellationToken ctk)
    {
        var res = await ClipboardData.GetDataAsync("Object Descriptor");
        using IRandomAccessStream randomAccessStream = res.As<IRandomAccessStream>();
        using var stream = randomAccessStream.AsStreamForRead();
        using BinaryReader reader = new(stream);
        byte[] bytes = reader.ReadBytes(Marshal.SizeOf(typeof(OBJECTDESCRIPTOR)));

        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        OBJECTDESCRIPTOR? descriptor = (OBJECTDESCRIPTOR?)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(OBJECTDESCRIPTOR));
        handle.Free();

        if (descriptor.HasValue is false)
            return;

        stream.Seek(0, SeekOrigin.Begin);
        bytes = reader.ReadBytes((int)descriptor.Value.cbSize);

        var typeName = Encoding.Unicode.GetString(bytes[Index.FromStart((int)descriptor.Value.dwFullUserTypeName)..]);
        meta.OriginalType = typeName.Split('\0')[0];
    }

    private static async Task HanleFiles(DataPackageView ClipboardData, ClipboardMetaInfomation meta, CancellationToken ctk)
    {
        IReadOnlyList<IStorageItem> list = await ClipboardData.GetStorageItemsAsync();
        meta.Files = list.Select(storageItem => storageItem.Path).ToArray();
    }

    private static async Task HanleHtml(DataPackageView ClipboardData, ClipboardMetaInfomation meta, CancellationToken ctk)
        => meta.Html = await ClipboardData.GetHtmlFormatAsync();

    private static async Task HanleText(DataPackageView ClipboardData, ClipboardMetaInfomation meta, CancellationToken ctk)
        => meta.Text = await ClipboardData.GetTextAsync();

    public ClipboardFactory(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        Logger = ServiceProvider.GetRequiredService<ILogger>();
        WebDav = ServiceProvider.GetRequiredService<IWebDav>();
    }

    public override async Task<ClipboardMetaInfomation> GetMetaInfomation(CancellationToken ctk)
    {
        ClipboardMetaInfomation meta = new();
        DataPackageView ClipboardData = Clipboard.GetContent();
        if (ClipboardData is null)
        {
            return meta;
        }

        for (int i = 0; ClipboardData.AvailableFormats.Count == 0 && i < 10; i++)
        {
            await Task.Delay(200, ctk);
            ClipboardData = Clipboard.GetContent();
            Logger.Write(LOG_TAG, "retry times: " + (i + 1));
        }

        if (ClipboardData.AvailableFormats.Count == 0)
        {
            Logger.Write(LOG_TAG, "ClipboardData.AvailableFormats.Count is 0");
            meta.Text = "";
            return meta;
        }

        int errortimes = 0;
        foreach (var handler in FormatHandlerlist)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (ClipboardData.AvailableFormats.Contains(handler.Key))
                    {
                        await handler.Value(ClipboardData, meta, ctk);
                    }
                }
                catch (Exception ex)
                {
                    errortimes += 1;
                    Logger.Write(LOG_TAG, ex.ToString());
                    Thread.Sleep(200);
                }
            }
        }

        Logger.Write(LOG_TAG, "Text: " + meta.Text ?? "");
        return meta;
    }
}
