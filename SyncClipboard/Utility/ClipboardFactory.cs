using SyncClipboard.Core.Clipboard;
using SyncClipboard.Service;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SyncClipboard.Service.ProfileFactory;
using System.Windows.Forms;
using System.Drawing;
using SyncClipboard.Core.Utilities.Image;
using SyncClipboard.Core.Utilities.Notification;

namespace SyncClipboard.Utility;

internal class ClipboardFactory : ClipboardFactoryBase
{
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
