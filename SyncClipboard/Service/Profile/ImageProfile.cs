using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    class ImageProfile : FileProfile
    {
        public ImageProfile(string filepath) : base(filepath)
        { }

        public override ClipboardType GetProfileType()
        {
            return ClipboardType.Image;
        }

        protected override void SetContentToLocalClipboard()
        {
            base.SetContentToLocalClipboard();
            // TODO
        }
    }
}
