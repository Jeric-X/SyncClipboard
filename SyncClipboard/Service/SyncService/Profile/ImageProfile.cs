using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Notification;
using System;
using System.Drawing;
using System.IO;
using Button = SyncClipboard.Core.Utilities.Notification.Button;
#nullable enable

namespace SyncClipboard.Service
{
    public class ImageProfile : FileProfile
    {
        public override ProfileType Type => ProfileType.Image;

        private readonly static string TEMP_FOLDER = Path.Combine(SyncService.LOCAL_FILE_FOLDER, "temp images");
        public ImageProfile(string filepath, IServiceProvider serviceProvider) : base(filepath, serviceProvider)
        {
        }

        public ImageProfile(ClipboardProfileDTO profileDTO, IServiceProvider serviceProvider) : base(profileDTO, serviceProvider)
        {
        }

        public static ImageProfile CreateFromImage(Image image, IServiceProvider serviceProvider)
        {
            if (!Directory.Exists(TEMP_FOLDER))
            {
                Directory.CreateDirectory(TEMP_FOLDER);
            }
            var filePath = Path.Combine(TEMP_FOLDER, $"{Path.GetRandomFileName()}.bmp");
            image.Save(filePath);

            return new ImageProfile(filePath, serviceProvider);
        }

        protected override void SetNotification(NotificationManager notification)
        {
            var path = fullPath ?? GetTempLocalFilePath();
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
}