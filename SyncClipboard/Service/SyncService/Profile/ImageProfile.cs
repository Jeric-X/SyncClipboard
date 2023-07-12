using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Notification;
using System;
using System.Drawing;
using System.IO;
using static SyncClipboard.Service.ProfileType;
using Button = SyncClipboard.Core.Utilities.Notification.Button;
#nullable enable

namespace SyncClipboard.Service
{
    public class ImageProfile : FileProfile
    {
        public override Core.Clipboard.ProfileType Type => Core.Clipboard.ProfileType.Image;

        private readonly static string TEMP_FOLDER = Path.Combine(SyncService.LOCAL_FILE_FOLDER, "temp images");
        public ImageProfile(string filepath, IServiceProvider serviceProvider) : base(filepath, serviceProvider)
        {
        }

        public ImageProfile(JsonProfile jsonProfile, IServiceProvider serviceProvider) : base(jsonProfile, serviceProvider)
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

        public override ClipboardType GetProfileType()
        {
            return ClipboardType.Image;
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