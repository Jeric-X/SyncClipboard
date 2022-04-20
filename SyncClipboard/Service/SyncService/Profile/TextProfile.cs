using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using SyncClipboard.Utility;
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

        public override bool Equals(Object obj)
        {
            if (obj is null)
            {
                return false;
            }

            try
            {
                TextProfile textprofile = (TextProfile)obj;
                return this.Text == textprofile.Text;
            }
            catch
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return this.Text.GetHashCode();
        }

        public override async Task UploadProfileAsync(IWebDav webdav)
        {
            await webdav.PutTextAsync(SyncService.REMOTE_RECORD_FILE, this.ToJsonString(), 0, 0).ConfigureAwait(false);
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
            if (Text.Substring(0, 4) == "http" || Text.Substring(0, 4) == "www.")
            {
                return () => System.Diagnostics.Process.Start(Text);
            }

            return null;
        }
    }
}
