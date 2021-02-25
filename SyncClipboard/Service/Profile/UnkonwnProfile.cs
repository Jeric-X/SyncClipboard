using System;
using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    class UnkonwnProfile : Profile
    {
        public UnkonwnProfile() { }

        public override string ToolTip()
        {
            throw new NotImplementedException("Do not support this type of clipboard");
        }

        public override void UploadProfile()
        {
            throw new NotImplementedException("Do not support this type of clipboard");
        }

        public override ClipboardType GetProfileType()
        {
            return ClipboardType.Unknown;
        }

        protected override void SetContentToLocalClipboard()
        {
            throw new NotImplementedException("Do not support this type of clipboard");
        }
    }
}