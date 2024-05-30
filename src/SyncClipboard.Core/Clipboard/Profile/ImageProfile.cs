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

    private static string ImageTemplateFolder => Path.Combine(LocalTemplateFolder, "temp images");

    private ImageProfile(string fullPath, string hash) : base(fullPath, hash)
    {
    }

    public ImageProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    public static async Task<ImageProfile> Create(IClipboardImage image, CancellationToken token)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                var fullPath = await Task.Run(() => SaveImageToFile(image)).WaitAsync(token);
                return await Create(fullPath, token);
            }
            catch when (!token.IsCancellationRequested)
            {
                Logger.Write($"SaveImageToFile wrong time {i + 1}");
                if (i > 5)
                    throw;
            }
            await Task.Delay(100, token);
        }
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
        var filePath = Path.Combine(ImageTemplateFolder, $"{Path.GetRandomFileName()}.{GetImageExtention()}");
        image.Save(filePath);
        return filePath;
    }

    private static string GetImageExtention()
    {
        return "png";
    }

    protected override void SetNotification(INotification notification)
    {
        ArgumentNullException.ThrowIfNull(FullPath);
        notification.SendImage(
            I18n.Strings.ClipboardImageUpdated,
            FileName,
            new Uri(FullPath),
            DefaultButton(),
#if WINDOWS
            new Button(I18n.Strings.OpenFolder, () => Sys.OpenFileInExplorer(FullPath)),
#endif
            new Button(I18n.Strings.Open, () => Sys.OpenWithDefaultApp(FullPath))
        );
    }
}