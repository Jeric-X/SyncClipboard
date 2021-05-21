using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using SyncClipboard.Utility;
using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    public class UnkonwnProfile : Profile
    {
        public override string ToolTip()
        {
            return "Do not support this type of clipboard";
        }

        public override void UploadProfile(IWebDav webdav)
        {
        }

        public override ClipboardType GetProfileType()
        {
            return ClipboardType.Unknown;
        }

        protected override DataObject CreateDataObject()
        {
            return null;
        }

        public override Task UploadProfileAsync(IWebDav webdav)
        {
            return Task.CompletedTask;
        }
    }
}