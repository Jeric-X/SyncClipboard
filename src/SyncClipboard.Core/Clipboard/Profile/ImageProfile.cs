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

    private readonly static string ImageTemplateFolder = Path.Combine(LocalTemplateFolder, "temp images");

    private ImageProfile(string fullPath, string hash) : base(fullPath, hash)
    {
    }

    public ImageProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    public static async Task<ImageProfile> Create(IClipboardImage image, CancellationToken token)
    {
        var fullPath = await Task.Run(() => SaveImageToFile(image)).WaitAsync(token);
        return await Create(fullPath, token);
    }

    public static new async Task<ImageProfile> Create(string fullPath, CancellationToken token)
    {
        var hash = await GetMD5HashFromFile(fullPath, token);
        return new ImageProfile(fullPath, hash);
    }

    private static string SaveImageToFile(IClipboardImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!Directory.Exists(ImageTemplateFolder))
        {
            Directory.CreateDirectory(ImageTemplateFolder);
        }
        var filePath = Path.Combine(ImageTemplateFolder, $"{Path.GetRandomFileName()}.png");
        image.Save(filePath);
        return filePath;
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