using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    class ImageProfile : FileProfile
    {
        private string fullpath = "";
        public ImageProfile(string filepath) : base(filepath)
        {
        }

        public ImageProfile(JsonProfile jsonProfile) : base(jsonProfile)
        {
        }

        public override ClipboardType GetProfileType()
        {
            return ClipboardType.Image;
        }

        protected override void SetContentToLocalClipboard()
        {
            base.SetContentToLocalClipboard();
            // string imagePath = GetTempLocalFilePath();
            // string html = $@"<img src=""file:///{imagePath}"">";
            // ClipboardHelper.CopyToClipboard(html, "");
        }
    }
}
