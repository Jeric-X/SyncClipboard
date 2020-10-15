
using SyncClipboard.Utility;
using System;
using System.Drawing;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SyncClipboard
{
    class Profile
    {
        public enum ClipboardType {
            Text,
            Image,
            None
        };

        public String FileName { get; set; }
        public String Text { get; set; }
        public ClipboardType Type { get; set; }

        private Image image;

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

        private static System.Threading.Mutex clipboardMutex = new System.Threading.Mutex();
        public static Profile CreateFromLocalClipboard()
        {
            Profile profile = new Profile();

            clipboardMutex.WaitOne();

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    IDataObject ClipboardData = Clipboard.GetDataObject();
                    profile.image = (Image)ClipboardData.GetData(DataFormats.Bitmap);
                    profile.Text = (string)ClipboardData.GetData(DataFormats.Text);
                    break;
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }

            clipboardMutex.ReleaseMutex();

            if (profile.image != null)
            {
                profile.Type = ClipboardType.Image;
            }

            if (profile.Text != "" && profile.Text != null)
            {
                profile.Type = ClipboardType.Text;
            }

            return profile;
        }

        public virtual void SetLocalClipboard()
        {
            clipboardMutex.WaitOne();
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
            
            clipboardMutex.ReleaseMutex();
        }

        public static bool operator == (Profile lhs, Profile rhs)
        {
            if (System.Object.ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (((object)lhs == null) || ((object)rhs == null))
            {
                return false;
            }

            if (lhs.Type != rhs.Type)
            {
                return false;
            }

            if (lhs.Type == ClipboardType.Text)
            {
                return lhs.Text == rhs.Text;
            }
            return true;
        }

        public static bool operator != (Profile lhs, Profile rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(Object obj)
        {
            return this == (Profile)obj;
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

    
        public Image GetImage()
        {
            return image;
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
    }
}
