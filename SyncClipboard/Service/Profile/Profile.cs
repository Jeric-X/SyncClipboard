using System;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    abstract class Profile
    {
        public String FileName { get; set; }
        public String Text { get; set; }
        //public ClipboardType Type { get; set; }


        public Profile()
        {
            FileName = "";
            Text = "";
        }

        public abstract ClipboardType GetProfileType();
        protected abstract DataObject CreateDataObject();
        public abstract string ToolTip();
        public abstract void UploadProfile();
        public virtual Action ExecuteProfile()
        {
            return null;
        }

        protected virtual void BeforeSetLocal() { }

        public void SetLocalClipboard()
        {
            BeforeSetLocal();

            var dataObject = CreateDataObject();
            if (dataObject is null)
            {
                return;
            }

            LocalClipboardLocker.Lock();
            Clipboard.SetDataObject(dataObject, true);
            LocalClipboardLocker.Unlock();
        }

        static private String ClipBoardTypeToString(ClipboardType type)
        {
            return Enum.GetName(typeof(ClipboardType), type);
        }

        public String ToJsonString()
        {
            JsonProfile jsonProfile = new JsonProfile();
            jsonProfile.File = FileName;
            jsonProfile.Clipboard = Text;
            jsonProfile.Type = ClipBoardTypeToString(GetProfileType());

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            return serializer.Serialize(jsonProfile);
        }

        public static bool operator ==(Profile lhs, Profile rhs)
        {
            if (System.Object.ReferenceEquals(lhs, rhs))
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
            str += "FileName" + FileName.ToString();
            str += "Text:" + Text.ToString();
            return str;
        }
    }
}
