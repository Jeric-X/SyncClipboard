using System.Drawing;
using static SyncClipboard.ProfileFactory;

namespace SyncClipboard.Service
{
    class ImageProfile : Profile
    {
        public Image image;
        public ImageProfile(Image image)
        {
            this.image = image;
        }

        protected override ClipboardType GetProfileType()
        {
            return ClipboardType.Image;
        }

        public override void UploadProfile()
        {
            HttpWebResponseUtility.PutImage(Config.GetImageUrl(), this.GetImage(), Config.TimeOut, Config.GetHttpAuthHeader());
            HttpWebResponseUtility.PutText(Config.GetProfileUrl(), this.ToJsonString(), Config.TimeOut, Config.GetHttpAuthHeader());
        }

        public override void SetLocalClipboard()
        {
            // LocalClipboardLocker.Lock();
            // TODO
            // LocalClipboardLocker.Unlock();
        }


        private Image GetImage()
        {
            return image;
        }
    }
}
