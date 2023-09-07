using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Notification;
using System.Drawing;

namespace SyncClipboard.Core.Clipboard;

public class ImageProfile : FileProfile
{
    public override ProfileType Type => ProfileType.Image;
    protected override IClipboardSetter<Profile> ClipboardSetter { get; set; }
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

    private Image? Image { get; set; }
    private string ImageTemplateFolder => Path.Combine(LocalTemplateFolder, "temp images");

    public ImageProfile(string filepath, IServiceProvider serviceProvider) : base(filepath, serviceProvider)
    {
        ClipboardSetter = serviceProvider.GetRequiredService<IClipboardSetter<ImageProfile>>();
    }

    public ImageProfile(ClipboardProfileDTO profileDTO, IServiceProvider serviceProvider) : base(profileDTO, serviceProvider)
    {
        ClipboardSetter = serviceProvider.GetRequiredService<IClipboardSetter<ImageProfile>>();
    }

    public ImageProfile(Image image, IServiceProvider serviceProvider) : base("", serviceProvider)
    {
        ClipboardSetter = serviceProvider.GetRequiredService<IClipboardSetter<ImageProfile>>();
        Image = image;
    }

    private void SaveImageToFile()
    {
        ArgumentNullException.ThrowIfNull(Image);
        if (!Directory.Exists(ImageTemplateFolder))
        {
            Directory.CreateDirectory(ImageTemplateFolder);
        }
        var filePath = Path.Combine(ImageTemplateFolder, $"{Path.GetRandomFileName()}.bmp");
        Image.Save(filePath);
        FullPath = filePath;
    }

    protected override void SetNotification(NotificationManager notification)
    {
        var path = FullPath ?? GetTempLocalFilePath();
        notification.SendImage(
            "图片同步成功",
            FileName,
            new Uri(path),
            DefaultButton(),
            new Button("打开文件夹", new Callbacker(Guid.NewGuid().ToString(), OpenInExplorer())),
            new Button("打开", new Callbacker(Guid.NewGuid().ToString(), (_) => Sys.OpenWithDefaultApp(path)))
        );
    }
}