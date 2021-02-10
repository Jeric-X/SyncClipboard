using System;
using System.Drawing;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SyncClipboard.Utility;
using static SyncClipboard.ProfileFactory;

namespace SyncClipboard
{
    class Profile
    {
        public String FileName { get; set; }
        public String Text { get; set; }
        public ClipboardType Type { get; set; }

        public Image image;

        public Profile()
        {
            FileName = "";
            Text = "";
            Type = ClipboardType.None;
        }

        public Profile(String jsonStr)
        {
            JsonProfile jsonProfile = null;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                jsonProfile = serializer.Deserialize<JsonProfile>(jsonStr);
                FileName = jsonProfile.File;
                Text = jsonProfile.Clipboard;
                Type = StringToClipBoardType(jsonProfile.Type);
            }
            catch (ArgumentException)
            {
                Log.Write("Existed profile file's format is wrong");
            }
        }

        private class JsonProfile
        {
            public String File { get; set; }
            public String Clipboard { get; set; }
            public String Type { get; set; }
        }

        static private ClipboardType StringToClipBoardType(String stringType)
        {
            ClipboardType type = ClipboardType.Text;
            try
            {
                type = (ClipboardType)Enum.Parse(typeof(ClipboardType), stringType);
            }
            catch
            {
                Log.Write("Profile Type is Wrong");
                throw new ArgumentException("Profile Type is Wrong");
            }

            return type;
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
            jsonProfile.Type = ClipBoardTypeToString(Type);

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            return serializer.Serialize(jsonProfile);
        }

        public static bool operator ==(Profile lhs, Profile rhs)
        {
            return Object.Equals(lhs, rhs);
        }

        public static bool operator !=(Profile lhs, Profile rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(Object obj)
        {
            if (System.Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj == null)
            {
                return false;
            }

            if (GetType() != obj.GetType())
            {
                return false;
            }

            Profile profile = (Profile)obj;
            if (profile.Type == ClipboardType.Text)
            {
                return this.Text == profile.Text;
            }

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
            str += "Type:" + Type.ToString();
            return str;
        }

        public virtual void SetLocalClipboard()
        {
            LocalClipboardLocker.Lock();
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetData(DataFormats.Text, this.Text);
                    break;
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }

            LocalClipboardLocker.Unlock();
        }


        public Image GetImage()
        {
            return image;
        }
    }
}
