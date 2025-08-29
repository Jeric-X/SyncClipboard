using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.Clipboard;

public class ImageProfile : FileProfile
{
    public override ProfileType Type => ProfileType.Image;
    protected override IClipboardSetter<Profile> ClipboardSetter
        => ServiceProvider.GetRequiredService<IClipboardSetter<ImageProfile>>();

    private static string ImageTemplateFolder => Path.Combine(LocalTemplateFolder, "temp images");

    private ImageProfile(string fullPath, string hash, bool contentControl = true)
        : base(fullPath, hash, contentControl)
    {
    }

    public ImageProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    public static async Task<ImageProfile> Create(IClipboardImage image, bool contentControl, CancellationToken token)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                var fullPath = await Task.Run(() => SaveImageToFile(image)).WaitAsync(token);
                return await Create(fullPath, contentControl, token);
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

    public static new async Task<ImageProfile> Create(string fullPath, bool contentControl, CancellationToken token)
    {
        var hash = await GetMD5HashFromFile(fullPath, contentControl, token);
        return new ImageProfile(fullPath, hash, contentControl);
    }

    public static new Task<ImageProfile> Create(string fullPath, CancellationToken token)
    {
        return Create(fullPath, true, token);
    }

    private static string SaveImageToFile(IClipboardImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!Directory.Exists(ImageTemplateFolder))
        {
            Directory.CreateDirectory(ImageTemplateFolder);
        }
        var filePath = Path.Combine(ImageTemplateFolder, $"Image_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Path.GetRandomFileName()}.{GetImageExtention()}");
        image.Save(filePath);
        return filePath;
    }

    private static string GetImageExtention()
    {
        return "png";
    }

    protected override void SetNotification(INotificationManager notificationManager)
    {
        ArgumentNullException.ThrowIfNull(FullPath);
        var notification = notificationManager.Create();
        notification.Title = I18n.Strings.ClipboardImageUpdated;
        notification.Message = FileName;
        notification.Image = new Uri(FullPath);
        notification.Buttons = [
            DefaultButton(),
            new ActionButton(I18n.Strings.OpenFolder, () => Sys.ShowPathInFileManager(FullPath)),
            new ActionButton(I18n.Strings.Open, () => Sys.OpenWithDefaultApp(FullPath))
        ];
        notification.Show();
    }

    public override HistoryRecord CreateHistoryRecord()
    {
        var record = base.CreateHistoryRecord();
        record.Type = ProfileType.Image;
        record.Text = FileName;
        return record;
    }
}