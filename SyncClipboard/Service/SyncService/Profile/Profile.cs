using System;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows.Forms;
using SyncClipboard.Utility.Web;
using static SyncClipboard.Service.ProfileType;
using System.Threading;

namespace SyncClipboard.Service
{
    public abstract class Profile
    {
        public String FileName { get; set; } = "";
        public String Text { get; set; } = "";
        //public ClipboardType Type { get; set; }

        public abstract ClipboardType GetProfileType();
        protected abstract DataObject CreateDataObject();
        public abstract string ToolTip();
        public abstract Task UploadProfileAsync(IWebDav webdav, CancellationToken cancelToken);
        public virtual Action ExecuteProfile()
        {
            return null;
        }

        protected virtual Task BeforeSetLocal() { return Task.CompletedTask; }

        public async Task SetLocalClipboard()
        {
            await BeforeSetLocal().ConfigureAwait(true);

            var dataObject = CreateDataObject();
            if (dataObject is null)
            {
                return;
            }

            lock (SyncService.localProfilemutex)
            {
                Clipboard.SetDataObject(dataObject, true);
            }
        }

        static private string ClipBoardTypeToString(ClipboardType type)
        {
            return Enum.GetName(typeof(ClipboardType), type);
        }

        public string ToJsonString()
        {
            JsonProfile jsonProfile = new JsonProfile
            {
                File = FileName,
                Clipboard = Text,
                Type = ClipBoardTypeToString(GetProfileType())
            };

            return JsonSerializer.Serialize(jsonProfile);
        }

        public static bool operator ==(Profile lhs, Profile rhs)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (lhs is null)
            {
                return rhs is null;
            }

            if (rhs is null)
            {
                return false;
            }

            if (lhs.GetType() != rhs.GetType())
            {
                return false;
            }

            return Object.Equals(lhs, rhs);
        }

        public static bool operator !=(Profile lhs, Profile rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(Object obj)
        {
            return true;
        }

        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        public override string ToString()
        {
            string str = "";
            str += "FileName" + FileName;
            str += "Text:" + Text;
            return str;
        }
    }
}
