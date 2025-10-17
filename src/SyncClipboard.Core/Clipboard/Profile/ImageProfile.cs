using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Clipboard;

public class ImageProfile : FileProfile
{
    public override ProfileType Type => ProfileType.Image;

    private static string ImageTemplateFolder => Path.Combine(LocalTemplateFolder, "temp images");

    private ImageProfile(string fullPath, string hash, bool contentControl = true)
        : base(fullPath, hash, contentControl)
    {
    }

    public ImageProfile(HistoryRecord record) : base(record)
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
                var tempPath = await Task.Run(() => SaveImageToFile(image)).WaitAsync(token);
                var imageProfile = await Create(tempPath, contentControl, token);

                // 如果启用历史记录，移动文件到历史记录文件夹
                var historyConfig = Config.GetConfig<HistoryConfig>();
                if (historyConfig.EnableHistory)
                {
                    var historyFolder = Path.Combine(Env.HistoryFileFolder, imageProfile.Hash);
                    Directory.CreateDirectory(historyFolder);

                    var fileName = Path.GetFileName(tempPath);
                    var historyPath = Path.Combine(historyFolder, fileName);

                    if (tempPath != historyPath)
                    {
                        File.Move(tempPath, historyPath);
                        imageProfile.FullPath = historyPath;
                    }
                }

                return imageProfile;
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

    public static Task<ImageProfile> Create(string fullPath, CancellationToken token)
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

    public override HistoryRecord CreateHistoryRecord()
    {
        var record = base.CreateHistoryRecord();
        record.Type = ProfileType.Image;
        record.Text = FileName;
        return record;
    }
}