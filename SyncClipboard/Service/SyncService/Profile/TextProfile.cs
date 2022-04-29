using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Web;
using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    public class TextProfile : Profile
    {
        public TextProfile(String text)
        {
            Text = text;
        }

        public override ClipboardType GetProfileType()
        {
            return ClipboardType.Text;
        }

        public override string ToolTip()
        {
            return Text;
        }

        protected override Task<bool> Same(Profile rhs, CancellationToken cancellationToken)
        {
            try
            {
                var textprofile = (TextProfile)rhs;
                return Task.FromResult(Text == textprofile.Text);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        public override async Task UploadProfileAsync(IWebDav webdav, CancellationToken cancelToken)
        {
            await webdav.PutText(SyncService.REMOTE_RECORD_FILE, this.ToJsonString(), cancelToken);
        }

        protected override DataObject CreateDataObject()
        {
            var dataObject = new DataObject();
            dataObject.SetData(DataFormats.Text, this.Text);
            return dataObject;
        }

        public override Action ExecuteProfile()
        {
            if (Text == null || Text.Length < 4)
            {
                return null;
            }
            if (Text[..4] == "http" || Text[..4] == "www.")
            {
                return () => Sys.OpenWithDefaultApp(Text);
            }

            return null;
        }
    }
}
