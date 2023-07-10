using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Image;

namespace SyncClipboard.Service;

public abstract class ClipboardFactoryBase : IClipboardFactory
{
    protected abstract ILogger Logger { get; set; }
    protected abstract UserConfig UserConfig { get; set; }

    public abstract MetaInfomation GetMetaInfomation();

    public Profile CreateProfile(MetaInfomation metaInfomation = null)
    {
        metaInfomation ??= GetMetaInfomation();

        if (metaInfomation.Files != null)
        {
            var filename = metaInfomation.Files[0];
            if (System.IO.File.Exists(filename))
            {
                if (ImageHelper.FileIsImage(filename))
                {
                    return new ImageProfile(filename, Logger, UserConfig);
                }
                return new FileProfile(filename, Logger, UserConfig);
            }
        }

        if (metaInfomation.Text != null)
        {
            return new TextProfile(metaInfomation.Text);
        }

        if (metaInfomation.Image != null)
        {
            return ImageProfile.CreateFromImage(metaInfomation.Image, Logger, UserConfig);
        }

        return new UnkonwnProfile();
    }
}
