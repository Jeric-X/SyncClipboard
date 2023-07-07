using ImageMagick;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Utility;
using SyncClipboard.Core.Utilities.Notification;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using static SyncClipboard.Service.ProfileType;
using Button = SyncClipboard.Core.Utilities.Notification.Button;
using SyncClipboard.Control;
#nullable enable

namespace SyncClipboard.Service
{
    public class ImageProfile : FileProfile
    {
        private readonly static string TEMP_FOLDER = Path.Combine(SyncService.LOCAL_FILE_FOLDER, "temp images");
        public ImageProfile(string filepath, NotificationManager notificationManager) : base(filepath, notificationManager)
        {
        }

        public ImageProfile(JsonProfile jsonProfile, IWebDav webdav, NotificationManager notificationManager) : base(jsonProfile, webdav, notificationManager)
        {
        }

        public static ImageProfile CreateFromImage(Image image, NotificationManager notificationManager)
        {
            if (!Directory.Exists(TEMP_FOLDER))
            {
                Directory.CreateDirectory(TEMP_FOLDER);
            }
            var filePath = Path.Combine(TEMP_FOLDER, $"{Path.GetRandomFileName()}.bmp");
            image.Save(filePath);

            return new ImageProfile(filePath, notificationManager);
        }

        public override ClipboardType GetProfileType()
        {
            return ClipboardType.Image;
        }

        private static void SetBitmap(DataObject dataObject, string imagePath)
        {
            using var image = new MagickImage(imagePath);
            dataObject.SetData(DataFormats.Bitmap, image.ToBitmap());
        }

        private static void SetHtml(DataObject dataObject, string imagePath)
        {
            string html = $@"<img src=""file:///{imagePath}"">";
            string clipboardHtml = ClipboardHtmlBuilder.GetClipboardHtml(html);
            dataObject.SetData(DataFormats.Html, clipboardHtml);
        }

        private const string clipboardQqFormat = @"<QQRichEditFormat>
<Info version=""1001"">
</Info>
<EditElement type=""1"" imagebiztype=""0"" textsummary="""" filepath=""<<<<<<"" shortcut="""">
</EditElement>
</QQRichEditFormat>";

        private static void SetQqFormat(DataObject dataObject, string imagePath)
        {
            string clipboardQq = clipboardQqFormat.Replace("<<<<<<", imagePath);
            MemoryStream ms = new(System.Text.Encoding.UTF8.GetBytes(clipboardQq));
            dataObject.SetData("QQ_Unicode_RichEdit_Format", ms);
        }

        protected override DataObject? CreateDataObject()
        {
            var dataObject = base.CreateDataObject();
            if (dataObject is null)
            {
                return null;
            }

            ArgumentNullException.ThrowIfNull(fullPath);
            SetHtml(dataObject, fullPath);
            SetQqFormat(dataObject, fullPath);
            SetBitmap(dataObject, fullPath);

            return dataObject;
        }

        protected override void AfterSetLocal()
        {
            var path = fullPath ?? GetTempLocalFilePath();
            NotificationManager.SendImage(
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