using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

#nullable enable
namespace SyncClipboard.ClipboardWinform;

internal class ClipboardFactory : ClipboardFactoryBase
{
    protected override ILogger Logger { get; set; }
    protected override UserConfig UserConfig { get; set; }
    protected override IServiceProvider ServiceProvider { get; set; }
    protected override IWebDav WebDav { get; set; }

    public ClipboardFactory(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        Logger = ServiceProvider.GetRequiredService<ILogger>();
        UserConfig = ServiceProvider.GetRequiredService<UserConfig>();
        WebDav = ServiceProvider.GetRequiredService<IWebDav>();
    }

    public override ClipboardMetaInfomation GetMetaInfomation()
    {
        ClipboardMetaInfomation meta = new();
        for (int i = 0; i < 3; i++)
        {
            try
            {
                IDataObject ClipboardData = Clipboard.GetDataObject();
                if (ClipboardData is null)
                {
                    return meta;
                }
                if (ClipboardData.GetFormats().Length == 0)
                {
                    meta.Text = "";
                    break;
                }
                meta.Image = (Image)ClipboardData.GetData(DataFormats.Bitmap);
                meta.Text = (string)ClipboardData.GetData(DataFormats.Text) ?? meta.Text;
                meta.Files = (string[])ClipboardData.GetData(DataFormats.FileDrop);
                meta.Html = (string)ClipboardData.GetData(DataFormats.Html);
                meta.Effects = (Core.Models.DragDropEffects?)(ClipboardData.GetData("Preferred DropEffect") as MemoryStream)?.ReadByte();
                break;
            }
            catch
            {
                Thread.Sleep(200);
            }
        }
        return meta;
    }
}
