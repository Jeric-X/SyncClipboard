using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.Clipboard;

public class ImageProfile : FileProfile
{
    public override ProfileType Type => ProfileType.Image;
    protected override IClipboardSetter<Profile> ClipboardSetter
        => ServiceProvider.GetRequiredService<IClipboardSetter<ImageProfile>>();
    public override string FileName
    {
        get
        {
            if (string.IsNullOrEmpty(base.FileName))
            {
                FileName = Path.GetFileName(FullPath)!;
            }
            return base.FileName;
        }
        set => base.FileName = value;
    }

    public override string? FullPath
    {
        get
        {
            if (string.IsNullOrEmpty(base.FullPath) && Image is not null)
            {
                SaveImageToFile();
            }
            return base.FullPath;
        }
        set => base.FullPath = value;
    }

    private IClipboardImage? Image { get; set; }
    private readonly static string ImageTemplateFolder = Path.Combine(LocalTemplateFolder, "temp images");

    public ImageProfile(string filepath) : base(filepath)
    {
    }

    public ImageProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    public ImageProfile(IClipboardImage image) : base()
    {
        Image = image;
    }

    private void SaveImageToFile()
    {
        ArgumentNullException.ThrowIfNull(Image);
        if (!Directory.Exists(ImageTemplateFolder))
        {
            Directory.CreateDirectory(ImageTemplateFolder);
        }
        var filePath = Path.Combine(ImageTemplateFolder, $"{Path.GetRandomFileName()}.png");
        Image.Save(filePath);
        FullPath = filePath;
    }

    protected override void SetNotification(INotification notification)
    {
        var path = FullPath ?? GetTempLocalFilePath();
        notification.SendImage(
            I18n.Strings.ClipboardImageUpdated,
            FileName,
            new Uri(path),
            DefaultButton(),
#if WINDOWS
            new Button(I18n.Strings.OpenFolder, OpenInExplorer),
#endif
            new Button(I18n.Strings.Open, () => Sys.OpenWithDefaultApp(path))
        );
    }
}