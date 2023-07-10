using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SyncClipboard.Core.Interfaces;
using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    public class UnkonwnProfile : Profile
    {
        public override Core.Clipboard.ProfileType Type => Core.Clipboard.ProfileType.Unknown;

        public override string ToolTip()
        {
            return "Do not support this type of clipboard";
        }

        public override ClipboardType GetProfileType()
        {
            return ClipboardType.Unknown;
        }

        protected override DataObject CreateDataObject()
        {
            return null;
        }

        public override Task UploadProfileAsync(IWebDav webdav, CancellationToken cancelToken)
        {
            return Task.CompletedTask;
        }

        protected override Task<bool> Same(Profile rhs, CancellationToken cancellationToken)
        {
            try
            {
                var _ = (TextProfile)rhs;
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }
}