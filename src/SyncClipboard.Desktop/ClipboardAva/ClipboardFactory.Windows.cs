using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal partial class ClipboardFactory
{
    [SupportedOSPlatform("windows")]
    private async Task<ClipboardMetaInfomation> HandleWindowsClipboard(CancellationToken token)
    {
        ClipboardMetaInfomation meta = new()
        {
            // 文字：使用 Avalonia 通用格式
            Text = await Clipboard.GetTextAsync(token)
        };

        // 图片：只读取 Bitmap
        var bitmap = await Clipboard.GetBitmapAsync(token);
        if (bitmap is not null)
        {
            meta.OriginalType = ClipboardMetaInfomation.ImageType;
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            meta.Image = ClipboardImage.TryCreateImage(stream.ToArray());
        }

        // 文件：使用 Avalonia 通用格式
        var files = await Clipboard.GetFilesAsync(token);
        if (files is not null && files.Length > 0)
        {
            meta.Files = files.Select(f => f.Path.LocalPath).ToArray();
        }

        return meta;
    }
}