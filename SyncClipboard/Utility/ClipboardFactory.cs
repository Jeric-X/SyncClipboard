using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Service;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SyncClipboard.Utility;

internal class ClipboardFactory : ClipboardFactoryBase
{
    protected override ILogger Logger { get; set; }
    protected override UserConfig UserConfig { get; set; }

    public ClipboardFactory(ILogger logger, UserConfig userConfig)
    {
        Logger = logger;
        UserConfig = userConfig;
    }

    public override MetaInfomation GetMetaInfomation()
    {
        MetaInfomation meta = new();
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
                meta.Effects = (Core.Clipboard.DragDropEffects?)(ClipboardData.GetData("Preferred DropEffect") as MemoryStream)?.ReadByte();
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
