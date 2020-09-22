
using System;
using System.Web.Script.Serialization;

namespace SyncClipboard
{
    class Profile
    {
        public enum ClipboardType {
            Text,
            Image
        };

        public String FileName { get; set; }
        public String Text { get; set; }
        public ClipboardType Type { get; set; }

        public Profile()
        {
            FileName = "";
            Text = "";
            Type = ClipboardType.Text;
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
                Console.WriteLine("Existed profile file's format is wrong");
                FileName = "";
                Text = "";
                Type = ClipboardType.Text;
            }   
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
                Console.WriteLine("Profile Type is Wrong");
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
