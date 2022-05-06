using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Notification;
using SyncClipboard.Utility.Web;
using static SyncClipboard.Service.ProfileType;
using Button = SyncClipboard.Utility.Notification.Button;
#nullable enable

namespace SyncClipboard.Service
{
    public class ImageProfile : FileProfile
    {
        private readonly static string TEMP_FOLDER = Path.Combine(SyncService.LOCAL_FILE_FOLDER, "temp images");
        public ImageProfile(string filepath) : base(filepath)
        {
        }

        public ImageProfile(JsonProfile jsonProfile, IWebDav webdav) : base(jsonProfile, webdav)
        {
        }

        public static ImageProfile CreateFromImage(Image image)
        {
            if (!Directory.Exists(TEMP_FOLDER))
            {
                Directory.CreateDirectory(TEMP_FOLDER);
            }
            var filePath = Path.Combine(TEMP_FOLDER, $"{Path.GetRandomFileName()}.bmp");
            image.Save(filePath);

            return new ImageProfile(filePath);
        }

        public override ClipboardType GetProfileType()
        {
            return ClipboardType.Image;
        }

        private static void SetBitmap(DataObject dataObject, string imagePath)
        {
            FileStream fileStream = new(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] bytes = new byte[fileStream.Length];
            fileStream.Read(bytes, 0, bytes.Length);
            fileStream.Close();

            Stream stream = new MemoryStream(bytes);

            var bitmap = new Bitmap(stream);
            stream.Close();

            dataObject.SetData(DataFormats.Bitmap, bitmap);
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
            Toast.SendImage(
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